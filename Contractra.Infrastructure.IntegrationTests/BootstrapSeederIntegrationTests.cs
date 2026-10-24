using Contractra.Application.Identity;
using Contractra.Application.Identity.Operations;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contractra.Infrastructure.IntegrationTests;

// One test database per class: the bootstrap admin is a singleton, so each scenario
// that needs "no admin yet" gets its own class and therefore its own database.

internal static class BootstrapSeederAssertions
{
    public const string Login = "Bootstrap.Admin";
    public const string Password = "Sup3r-Secret-Bootstrap!";

    public static readonly IReadOnlyDictionary<string, string?> Settings = new Dictionary<string, string?>
    {
        ["Bootstrap:AdminLoginIdentifier"] = Login,
        ["Bootstrap:AdminPassword"] = Password,
    };

    public static async Task AssertCreatedAsync(PostgresTestDatabaseFixture fixture, ServiceProvider provider)
    {
        await using var read = fixture.CreateDbContext();
        var admin = await read.Users.Include(u => u.Roles).Include(u => u.Permissions).SingleAsync(u => u.IsBootstrapAdmin);

        Assert.Equal(UserStatus.Active, admin.Status);
        Assert.False(admin.MustChangePassword);
        Assert.Null(admin.TemporaryPasswordExpiresAt);
        Assert.NotEqual(PasswordVerificationResult.Failed, provider.GetRequiredService<IPasswordHasher>().Verify(admin.PasswordHash, Password));
        Assert.DoesNotContain(Password, admin.PasswordHash);

        var adminRole = await read.Roles.SingleAsync(r => r.Code == new RoleCode("Admin"));
        var payrollView = await read.Permissions.SingleAsync(p => p.Code == new PermissionCode(PermissionCode.PayrollView));
        Assert.Equal(adminRole.Id, Assert.Single(admin.Roles).RoleId);
        Assert.Equal(payrollView.Id, Assert.Single(admin.Permissions).PermissionId);

        // C3 / BR-235: Payroll.View reaches the admin only by the direct grant.
        Assert.False(await read.RolePermissions.AnyAsync(rp => rp.PermissionId == payrollView.Id));

        var events = await read.AuditEvents.Where(e => e.EntityId == admin.Id).OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(
            new[] { AuditActionType.UserCreated, AuditActionType.RoleAssigned, AuditActionType.PayrollViewGranted }.OrderBy(a => a),
            events.Select(e => e.ActionType).OrderBy(a => a));

        var correlationIds = events.Select(e => e.CorrelationId).Distinct().ToList();
        Assert.Single(correlationIds);
        foreach (var auditEvent in events)
        {
            Assert.Equal(AuditActorType.System, auditEvent.ActorType);
            Assert.Null(auditEvent.ActorUserId);
            Assert.Null(auditEvent.ActorRoleCode);
            Assert.Equal(AuditSource.SystemJob, auditEvent.Source);
            Assert.Equal(SeedBootstrapAdminUseCase.SeedReason, auditEvent.Reason);
            Assert.Equal($"{auditEvent.ActionType}:bootstrap:{admin.Id}", auditEvent.IdempotencyKey);
            Assert.DoesNotContain(Password, auditEvent.Snapshot ?? string.Empty);
        }
    }
}

public class BootstrapSeederCreatesAndRepeatsIntegrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public BootstrapSeederCreatesAndRepeatsIntegrationTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Startup_CreatesTheBootstrapAdminWithItsThreeAuditEvents_AndARepeatIsANoOpThatNeverOverwritesThePassword()
    {
        await using var first = Slice5Support.BuildProvider(_fixture, settings: BootstrapSeederAssertions.Settings);
        await first.GetRequiredService<StartupInitializer>().RunAsync();
        await BootstrapSeederAssertions.AssertCreatedAsync(_fixture, first);

        // A second start with a different configured password: nothing changes.
        var different = new Dictionary<string, string?>
        {
            ["Bootstrap:AdminLoginIdentifier"] = BootstrapSeederAssertions.Login,
            ["Bootstrap:AdminPassword"] = "A-Completely-Different-1",
        };
        await using var second = Slice5Support.BuildProvider(_fixture, settings: different);
        await second.GetRequiredService<StartupInitializer>().RunAsync();

        await BootstrapSeederAssertions.AssertCreatedAsync(_fixture, second);   // still the original password, one admin, three events
        await using var read = _fixture.CreateDbContext();
        Assert.Equal(1, await read.Users.CountAsync());
        Assert.Equal(3, await read.AuditEvents.CountAsync());
    }
}

public class BootstrapSeederMissingConfigurationIntegrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public BootstrapSeederMissingConfigurationIntegrationTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Startup_WithMissingConfiguration_LogsAWarningAndCreatesNoUserAndNoAuditRow()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = Slice5Support.BuildProvider(_fixture, logs: logs);

        await provider.GetRequiredService<StartupInitializer>().RunAsync();

        await using var read = _fixture.CreateDbContext();
        Assert.False(await read.Users.AnyAsync());
        Assert.False(await read.AuditEvents.AnyAsync());
        Assert.Contains(logs.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Text.Contains("Bootstrap:AdminPassword"));
    }

    [Fact]
    public async Task Startup_WithAPasswordBreakingTheRules_RefusesAndLogsTheRuleNeverTheValue()
    {
        var logs = new CapturingLoggerProvider();
        var settings = new Dictionary<string, string?>
        {
            ["Bootstrap:AdminLoginIdentifier"] = "bootstrap.admin",
            ["Bootstrap:AdminPassword"] = "Sh0rt",
        };
        await using var provider = Slice5Support.BuildProvider(_fixture, settings: settings, logs: logs);

        await provider.GetRequiredService<StartupInitializer>().RunAsync();

        await using var read = _fixture.CreateDbContext();
        Assert.False(await read.Users.AnyAsync());
        Assert.Contains(logs.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error && e.Text.Contains("Bootstrap:AdminPassword"));
        Assert.DoesNotContain(logs.Entries, e => e.Text.Contains("Sh0rt", StringComparison.Ordinal));
    }
}

public class BootstrapSeederRollbackIntegrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public BootstrapSeederRollbackIntegrationTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task WhenTheAuditSaveFails_NoUserRoleGrantOrAuditRowSurvives_StartupFails_AndALaterStartSucceeds()
    {
        await using (var failing = Slice5Support.BuildProvider(_fixture, settings: BootstrapSeederAssertions.Settings, failingAuditWriter: true))
        {
            var exception = await Record.ExceptionAsync(() => failing.GetRequiredService<StartupInitializer>().RunAsync());

            var update = Assert.IsAssignableFrom<DbUpdateException>(exception);
            Assert.Equal("23503", Assert.IsAssignableFrom<Npgsql.PostgresException>(update.InnerException).SqlState);
        }

        await using (var read = _fixture.CreateDbContext())
        {
            Assert.False(await read.Users.AnyAsync());
            Assert.False(await read.UserRoles.AnyAsync());
            Assert.False(await read.UserPermissions.AnyAsync());
            Assert.False(await read.AuditEvents.AnyAsync());
        }

        // The failed run left nothing behind, so the next start creates the admin normally.
        await using var healthy = Slice5Support.BuildProvider(_fixture, settings: BootstrapSeederAssertions.Settings);
        await healthy.GetRequiredService<StartupInitializer>().RunAsync();
        await BootstrapSeederAssertions.AssertCreatedAsync(_fixture, healthy);
    }
}

public class BootstrapSeederRaceIntegrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public BootstrapSeederRaceIntegrationTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task TwoInstancesStartingTogether_LeaveExactlyOneAdminAndThreeAuditEvents_AndNeitherFails()
    {
        await using var a = Slice5Support.BuildProvider(_fixture, settings: BootstrapSeederAssertions.Settings);
        await using var b = Slice5Support.BuildProvider(_fixture, settings: BootstrapSeederAssertions.Settings);

        await Task.WhenAll(
            a.GetRequiredService<StartupInitializer>().RunAsync(),
            b.GetRequiredService<StartupInitializer>().RunAsync());

        await BootstrapSeederAssertions.AssertCreatedAsync(_fixture, a);
        await using var read = _fixture.CreateDbContext();
        Assert.Equal(1, await read.Users.CountAsync());
        Assert.Equal(3, await read.AuditEvents.CountAsync());
    }
}
