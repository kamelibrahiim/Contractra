using Contractra.Infrastructure.Persistence.Audit;
using Contractra.Infrastructure.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Policy over self-check reports. These use hand-built reports, so they prove the
/// decision rule only - NOT what PostgreSQL grants. The real-database behaviour is
/// <see cref="StartupSelfCheckIntegrationTests"/>; a compliant application role
/// cannot be created by the tests (roles are cluster-wide objects; repository ADR 0006 §7.3).
/// </summary>
public class AuditPrivilegePolicyTests
{
    private static AuditPrivilegeReport Safe() => new("audit_events", false, false, false, false, false, false, false);

    [Fact]
    public void ACompliantRole_HasNoViolations() => Assert.Empty(AuditPrivilegePolicy.Violations([Safe()]));

    [Theory]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    [InlineData("TRUNCATE")]
    [InlineData("TRIGGER")]
    [InlineData("owner")]
    [InlineData("member of the owner role")]
    [InlineData("superuser")]
    public void EachUnsafeCondition_IsReportedByName(string condition)
    {
        var report = condition switch
        {
            "UPDATE" => Safe() with { CanUpdate = true },
            "DELETE" => Safe() with { CanDelete = true },
            "TRUNCATE" => Safe() with { CanTruncate = true },
            "TRIGGER" => Safe() with { CanTrigger = true },
            "owner" => Safe() with { IsOwner = true },
            "member of the owner role" => Safe() with { HasOwnerRoleMembership = true },
            _ => Safe() with { IsSuperuser = true },
        };

        var violation = Assert.Single(AuditPrivilegePolicy.Violations([report]));
        Assert.Equal($"audit_events: {condition}", violation);
    }

    [Fact]
    public void SeveralConditions_AreListedTogether() =>
        Assert.Equal(
            "audit_events: UPDATE, DELETE, owner",
            Assert.Single(AuditPrivilegePolicy.Violations([Safe() with { CanUpdate = true, CanDelete = true, IsOwner = true }])));
}

/// <summary>Startup ordering with fake steps: no database involved.</summary>
public class StartupInitializerOrderingTests
{
    private sealed class RecordingStep : IStartupStep
    {
        private readonly List<string> _log;
        private readonly bool _fails;

        public RecordingStep(string name, int order, List<string> log, bool prerequisite = false, bool fails = false)
        {
            Name = name;
            Order = order;
            IsPrerequisiteForCommands = prerequisite;
            _log = log;
            _fails = fails;
        }

        public string Name { get; }

        public int Order { get; }

        public bool IsPrerequisiteForCommands { get; }

        public Task RunAsync(IServiceProvider scopedServices, CancellationToken cancellationToken)
        {
            _log.Add(Name);
            return _fails ? throw new InvalidOperationException($"{Name} failed") : Task.CompletedTask;
        }
    }

    private static StartupInitializer Build(params IStartupStep[] steps) =>
        new(steps, new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

    [Fact]
    public async Task StepsRunInOrder_NotInRegistrationOrder()
    {
        var log = new List<string>();
        var initializer = Build(new RecordingStep("seed", 100, log), new RecordingStep("check", 0, log));

        await initializer.RunAsync();

        Assert.Equal(new[] { "check", "seed" }, log);
    }

    [Fact]
    public async Task AFailingFirstStep_StopsStartup_AndTheSeederNeverRuns()
    {
        var log = new List<string>();
        var initializer = Build(new RecordingStep("check", 0, log, fails: true), new RecordingStep("seed", 100, log));

        await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.RunAsync());

        Assert.Equal(new[] { "check" }, log);
    }

    [Fact]
    public async Task CommandsRunOnlyThePrerequisiteSteps()
    {
        var log = new List<string>();
        var initializer = Build(new RecordingStep("check", 0, log, prerequisite: true), new RecordingStep("seed", 100, log));

        await initializer.RunPrerequisitesForCommandsAsync();

        Assert.Equal(new[] { "check" }, log);
    }

    [Fact]
    public void TheRealRegistration_PutsTheSelfCheckBeforeTheSeeder()
    {
        var services = new ServiceCollection();
        var configuration = new InMemoryConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=unused;Database=unused",
            ["Business:TimeZone"] = "Africa/Cairo",
        });
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddInfrastructure(configuration);
        services.AddStartupInitialization(failClosedOnUnsafeAuditPrivileges: true);
        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.Equal(
            new[] { "AuditPrivilegeSelfCheck", "BootstrapAdminSeeder" },
            provider.GetRequiredService<StartupInitializer>().StepNames);
    }
}

/// <summary>
/// The self-check against a real PostgreSQL 18 test database. The test role owns the
/// ephemeral database and so owns its tables: that is the non-compliant configuration
/// ADR 0006 §7.3 describes, which makes it the one real non-compliant case we can build.
/// </summary>
public class StartupSelfCheckIntegrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public StartupSelfCheckIntegrationTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task TheRealQuery_ReportsTheTestRoleAsTheOwner_SoItIsNonCompliant()
    {
        await using var dbContext = _fixture.CreateDbContext();

        var reports = await AuditPrivilegeSelfCheck.QueryAsync(dbContext);

        var violations = AuditPrivilegePolicy.Violations(reports);
        Assert.Contains("owner", Assert.Single(violations));
    }

    [Fact]
    public async Task OutsideProduction_AnUnsafeRole_OnlyWarns_ThenTheSeederRuns()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = Slice5Support.BuildProvider(
            _fixture, failClosedOnUnsafeAuditPrivileges: false, settings: BootstrapSeederAssertions.Settings, logs: logs);

        await provider.GetRequiredService<StartupInitializer>().RunAsync();

        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Category.EndsWith(nameof(AuditPrivilegeStartupStep)));
        await using var read = _fixture.CreateDbContext();
        Assert.True(await read.Users.AnyAsync(u => u.IsBootstrapAdmin));   // the seeder ran after the check
    }

    [Fact]
    public async Task TheSelfCheckRunsBeforeTheSeederWrites_ProvenByTheLogOrder()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = Slice5Support.BuildProvider(
            _fixture, failClosedOnUnsafeAuditPrivileges: false, settings: BootstrapSeederAssertions.Settings, logs: logs);

        await provider.GetRequiredService<StartupInitializer>().RunAsync();

        var checkIndex = logs.Entries.FindIndex(e => e.Category.EndsWith(nameof(AuditPrivilegeStartupStep)));
        var seedIndex = logs.Entries.FindIndex(e => e.Category.EndsWith("SeedBootstrapAdminUseCase"));
        Assert.True(checkIndex >= 0 && seedIndex >= 0, "Both steps must have logged.");
        Assert.True(checkIndex < seedIndex);
    }
}

/// <summary>Own class, so own database: "the seeder did not run" needs a database no other test has seeded.</summary>
public class StartupSelfCheckFailClosedIntegrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public StartupSelfCheckFailClosedIntegrationTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task InProduction_AnUnsafeRole_StopsStartup_AndTheBootstrapSeederNeverRuns()
    {
        await using var provider = Slice5Support.BuildProvider(
            _fixture, failClosedOnUnsafeAuditPrivileges: true, settings: BootstrapSeederAssertions.Settings);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<StartupInitializer>().RunAsync());

        Assert.Contains("audit", exception.Message);
        Assert.DoesNotContain(BootstrapSeederAssertions.Password, exception.Message);
        await using var read = _fixture.CreateDbContext();
        Assert.False(await read.Users.AnyAsync());           // the seeder did not run
        Assert.False(await read.AuditEvents.AnyAsync());
    }
}
