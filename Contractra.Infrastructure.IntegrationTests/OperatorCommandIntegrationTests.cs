using System.Security.Cryptography;
using Contractra.Application.Identity;
using Contractra.Application.Identity.Auth;
using Contractra.Application.Identity.UseCases;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.Persistence.Throttling;
using Contractra.Infrastructure.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contractra.Infrastructure.IntegrationTests;

internal static class OperatorCommandSeeding
{
    public static async Task<(Guid UserId, string Login, string PasswordHash, int SecurityVersion, Guid[] SessionIds)> SeedUserAsync(
        PostgresTestDatabaseFixture fixture, ServiceProvider provider, UserStatus status = UserStatus.Active, bool mustChange = false, int sessions = 2)
    {
        var hasher = provider.GetRequiredService<IPasswordHasher>();
        var login = $"Op.{Guid.NewGuid():N}";
        var user = mustChange
            ? new User(new LoginIdentifier(login), status, hasher.Hash("Tmp-Passw0rd-1"), true, DateTimeOffset.UtcNow.AddHours(48))
            : new User(new LoginIdentifier(login), status, hasher.Hash("OldPassw0rd1"), false);
        var rows = Enumerable.Range(0, sessions)
            .Select(_ => AuthSession.Start(
                user.Id, ClientApp.Admin, ClientPlatform.Web, RandomNumberGenerator.GetBytes(32),
                DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddDays(7)))
            .ToArray();

        await using var dbContext = fixture.CreateDbContext();
        dbContext.Users.Add(user);
        dbContext.AuthSessions.AddRange(rows);
        await dbContext.SaveChangesAsync();
        return (user.Id, login, user.PasswordHash, user.SecurityVersion, rows.Select(r => r.Id).ToArray());
    }

    public static async Task<(Guid UserId, string PasswordHash, int SecurityVersion, Guid[] SessionIds)> SeedBootstrapAdminAsync(
        PostgresTestDatabaseFixture fixture, ServiceProvider provider)
    {
        var hasher = provider.GetRequiredService<IPasswordHasher>();
        await using var dbContext = fixture.CreateDbContext();
        var adminRole = await dbContext.Roles.SingleAsync(r => r.Code == new RoleCode("Admin"));
        var payrollView = await dbContext.Permissions.SingleAsync(p => p.Code == new PermissionCode(PermissionCode.PayrollView));
        var now = DateTimeOffset.UtcNow;
        var admin = User.CreateBootstrapAdmin(
            new LoginIdentifier($"Boot.{Guid.NewGuid():N}"), hasher.Hash("Original-Password-1"), adminRole, payrollView, now, now);
        var sessions = Enumerable.Range(0, 2)
            .Select(_ => AuthSession.Start(
                admin.Id, ClientApp.Admin, ClientPlatform.Web, RandomNumberGenerator.GetBytes(32), now.AddMinutes(-10), now.AddDays(7)))
            .ToArray();
        dbContext.Users.Add(admin);
        dbContext.AuthSessions.AddRange(sessions);
        await dbContext.SaveChangesAsync();
        return (admin.Id, admin.PasswordHash, admin.SecurityVersion, sessions.Select(s => s.Id).ToArray());
    }
}

/// <summary>
/// issue-password-reset against PostgreSQL 18 through the real command runner
/// (contract v1.5 §10A, §13, §18; repository ADR 0006 §5.3, §6, E34).
/// </summary>
public class OperatorIssuePasswordResetIntegrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string Reason = "User lost the device holding the password manager.";

    private readonly PostgresTestDatabaseFixture _fixture;

    public OperatorIssuePasswordResetIntegrationTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task IssueResetCommand_CommitsTheTicketThePasswordInvalidationTheRevocationAndTheAuditEventTogether_AndThePrintedCodeCompletesTheReset()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = Slice5Support.BuildProvider(_fixture, logs: logs);
        var seeded = await OperatorCommandSeeding.SeedUserAsync(_fixture, provider);

        var (exit, output, error) = await Slice5Support.RunCommandAsync(
            provider, "issue-password-reset", "--login", seeded.Login.ToUpperInvariant(), "--reason", Reason);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error);
        var correlationId = Slice5Support.CorrelationIdIn(output);
        var code = Slice5Support.ResetCodeIn(output);
        Assert.NotEmpty(correlationId);
        Assert.NotEmpty(code);

        await using (var read = _fixture.CreateDbContext())
        {
            var user = await read.Users.SingleAsync(u => u.Id == seeded.UserId);
            Assert.False(user.HasUsablePassword);
            Assert.Equal(seeded.SecurityVersion + 1, user.SecurityVersion);

            var ticket = await read.PasswordResetTickets.SingleAsync(t => t.UserId == seeded.UserId);
            Assert.True(ticket.IsOpen);
            Assert.Equal("operator", ticket.Channel);
            Assert.Null(ticket.IssuedByUserId);
            Assert.Equal(provider.GetRequiredService<IResetCodeGenerator>().Hash(code), ticket.CodeHash);

            Assert.All(await read.AuthSessions.Where(s => s.UserId == seeded.UserId).ToListAsync(), s =>
            {
                Assert.NotNull(s.RevokedAt);
                Assert.Equal(SessionRevokeReason.PasswordResetIssued, s.RevokeReason);
            });

            var auditEvent = await read.AuditEvents.SingleAsync(e => e.EntityId == seeded.UserId);
            Assert.Equal(AuditActionType.PasswordResetIssued, auditEvent.ActionType);
            Assert.Equal(AuditActorType.System, auditEvent.ActorType);
            Assert.Null(auditEvent.ActorUserId);
            Assert.Null(auditEvent.ActorRoleCode);
            Assert.Equal(AuditSource.SystemJob, auditEvent.Source);
            Assert.Equal(Reason, auditEvent.Reason);
            Assert.Equal(correlationId, auditEvent.CorrelationId);
            Assert.Equal($"PasswordResetIssued:command:{correlationId}", auditEvent.IdempotencyKey);
            Assert.Contains(ticket.Id.ToString(), auditEvent.Snapshot);
            Assert.DoesNotContain(code, auditEvent.Snapshot);
        }

        // The secret was printed once and went nowhere else.
        Assert.DoesNotContain(logs.Entries, e => e.Text.Contains(code, StringComparison.OrdinalIgnoreCase));

        // End to end: the printed code is what the user needs to complete the reset (§5.6).
        await using var scope = provider.CreateAsyncScope();
        var completed = await scope.ServiceProvider.GetRequiredService<CompletePasswordResetUseCase>().ExecuteAsync(
            new CompletePasswordResetRequest(seeded.Login, code, "BrandNewPassw0rd", "corr-complete", AuditSource.Web));
        Assert.True(completed.IsSuccess);
    }

    [Fact]
    public async Task IssueResetCommand_RunTwice_SupersedesTheFirstTicket_AndAuditsEachRunUnderItsOwnCorrelationAndKey()
    {
        await using var provider = Slice5Support.BuildProvider(_fixture);
        var seeded = await OperatorCommandSeeding.SeedUserAsync(_fixture, provider);

        var first = await Slice5Support.RunCommandAsync(provider, "issue-password-reset", "--login", seeded.Login, "--reason", Reason);
        var second = await Slice5Support.RunCommandAsync(provider, "issue-password-reset", "--login", seeded.Login, "--reason", Reason);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.NotEqual(Slice5Support.CorrelationIdIn(first.Output), Slice5Support.CorrelationIdIn(second.Output));
        Assert.NotEqual(Slice5Support.ResetCodeIn(first.Output), Slice5Support.ResetCodeIn(second.Output));

        await using var read = _fixture.CreateDbContext();
        var tickets = await read.PasswordResetTickets.Where(t => t.UserId == seeded.UserId).ToListAsync();
        Assert.Equal(2, tickets.Count);
        Assert.Single(tickets, t => t.IsOpen);
        Assert.Equal(2, await read.AuditEvents.CountAsync(e => e.EntityId == seeded.UserId));
        Assert.Equal(
            2,
            await read.AuditEvents.Where(e => e.EntityId == seeded.UserId).Select(e => e.IdempotencyKey).Distinct().CountAsync());
    }

    [Fact]
    public async Task IssueResetCommand_WhenTheAuditSaveFails_RollsEverythingBack_PrintsNoCode_AndExitsNonZero()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = Slice5Support.BuildProvider(_fixture, logs: logs, failingAuditWriter: true);
        var seeded = await OperatorCommandSeeding.SeedUserAsync(_fixture, provider);

        var (exit, output, error) = await Slice5Support.RunCommandAsync(
            provider, "issue-password-reset", "--login", seeded.Login, "--reason", Reason);

        Assert.Equal(1, exit);
        Assert.DoesNotContain("Reset code", output);
        Assert.Contains("nothing was committed", error);

        await using var read = _fixture.CreateDbContext();
        var user = await read.Users.SingleAsync(u => u.Id == seeded.UserId);
        Assert.Equal(seeded.PasswordHash, user.PasswordHash);
        Assert.Equal(seeded.SecurityVersion, user.SecurityVersion);
        Assert.False(await read.PasswordResetTickets.AnyAsync(t => t.UserId == seeded.UserId));
        Assert.All(await read.AuthSessions.Where(s => s.UserId == seeded.UserId).ToListAsync(), s => Assert.Null(s.RevokedAt));
        Assert.False(await read.AuditEvents.AnyAsync(e => e.EntityId == seeded.UserId));
    }

    [Fact]
    public async Task IssueResetCommand_RefusesDisabledAndInitialPasswordAndUnknownAccounts_ChangingNothing()
    {
        await using var provider = Slice5Support.BuildProvider(_fixture);
        var disabled = await OperatorCommandSeeding.SeedUserAsync(_fixture, provider, status: UserStatus.Disabled);
        var initial = await OperatorCommandSeeding.SeedUserAsync(_fixture, provider, mustChange: true);

        foreach (var login in new[] { disabled.Login, initial.Login, "no.such.user" })
        {
            var (exit, _, error) = await Slice5Support.RunCommandAsync(provider, "issue-password-reset", "--login", login, "--reason", Reason);
            Assert.Equal(1, exit);
            Assert.Contains("Nothing was changed", error);
        }

        await using var read = _fixture.CreateDbContext();
        var ids = new[] { disabled.UserId, initial.UserId };
        Assert.False(await read.PasswordResetTickets.AnyAsync(t => ids.Contains(t.UserId)));
        Assert.False(await read.AuditEvents.AnyAsync(e => ids.Contains(e.EntityId)));
        Assert.Equal(disabled.PasswordHash, (await read.Users.SingleAsync(u => u.Id == disabled.UserId)).PasswordHash);
        Assert.Equal(initial.PasswordHash, (await read.Users.SingleAsync(u => u.Id == initial.UserId)).PasswordHash);
    }

    [Fact]
    public async Task IssueResetCommand_RefusesTheBootstrapAdmin_ChangingAndAuditingNothing()
    {
        await using var provider = Slice5Support.BuildProvider(_fixture);
        var admin = await OperatorCommandSeeding.SeedBootstrapAdminAsync(_fixture, provider);
        string login;
        await using (var lookup = _fixture.CreateDbContext())
        {
            login = (await lookup.Users.SingleAsync(u => u.Id == admin.UserId)).LoginIdentifier.Value;
        }

        var (exit, _, error) = await Slice5Support.RunCommandAsync(provider, "issue-password-reset", "--login", login, "--reason", Reason);

        Assert.Equal(1, exit);
        Assert.Contains("users.bootstrap_account_protected", error);
        await using var read = _fixture.CreateDbContext();
        Assert.Equal(admin.PasswordHash, (await read.Users.SingleAsync(u => u.Id == admin.UserId)).PasswordHash);
        Assert.False(await read.PasswordResetTickets.AnyAsync(t => t.UserId == admin.UserId));
        Assert.False(await read.AuditEvents.AnyAsync(e => e.EntityId == admin.UserId));
        Assert.All(await read.AuthSessions.Where(s => s.UserId == admin.UserId).ToListAsync(), s => Assert.Null(s.RevokedAt));
    }

    [Fact]
    public async Task Commands_RunTheAuditPrivilegeSelfCheckFirst_AndRefuseToWriteWhenItFailsClosed()
    {
        await using var provider = Slice5Support.BuildProvider(_fixture, failClosedOnUnsafeAuditPrivileges: true);
        var seeded = await OperatorCommandSeeding.SeedUserAsync(_fixture, provider);

        var (exit, output, error) = await Slice5Support.RunCommandAsync(
            provider, "issue-password-reset", "--login", seeded.Login, "--reason", Reason);

        Assert.Equal(1, exit);
        Assert.DoesNotContain("Reset code", output);
        await using var read = _fixture.CreateDbContext();
        Assert.Equal(seeded.PasswordHash, (await read.Users.SingleAsync(u => u.Id == seeded.UserId)).PasswordHash);
        Assert.False(await read.AuditEvents.AnyAsync(e => e.EntityId == seeded.UserId));
    }

    [Theory]
    [InlineData("issue-password-reset", "--login", "someone")]                                   // no --reason
    [InlineData("issue-password-reset", "--reason", "because")]                                  // no --login
    [InlineData("issue-password-reset", "--login", "someone", "--reason", "x", "--extra", "y")]  // unknown argument
    [InlineData("issue-password-reset", "--login", "a", "--login", "b", "--reason", "x")]        // repeated argument
    [InlineData("reset-bootstrap-password")]                                                     // no --reason
    [InlineData("reset-bootstrap-password", "--login", "someone", "--reason", "x")]              // --login is not allowed here
    public async Task BadUsage_ExitsWithTwo_AndChangesNothing(params string[] args)
    {
        await using var provider = Slice5Support.BuildProvider(_fixture);
        await using var before = _fixture.CreateDbContext();
        var eventsBefore = await before.AuditEvents.CountAsync();

        var (exit, output, error) = await Slice5Support.RunCommandAsync(provider, args);

        Assert.Equal(2, exit);
        Assert.Contains("Usage", error);
        Assert.DoesNotContain("shown once", output);
        await using var read = _fixture.CreateDbContext();
        Assert.Equal(eventsBefore, await read.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task AReasonWithPaddingIsRefused_BeforeAnythingIsWritten()
    {
        await using var provider = Slice5Support.BuildProvider(_fixture);
        var seeded = await OperatorCommandSeeding.SeedUserAsync(_fixture, provider);

        var (exit, _, error) = await Slice5Support.RunCommandAsync(
            provider, "issue-password-reset", "--login", seeded.Login, "--reason", " padded ");

        Assert.Equal(1, exit);
        Assert.Contains("validation.failed", error);
        await using var read = _fixture.CreateDbContext();
        Assert.False(await read.AuditEvents.AnyAsync(e => e.EntityId == seeded.UserId));
    }
}

/// <summary>reset-bootstrap-password against PostgreSQL 18 (contract v1.5 §11, owner decisions R19, R26, R29, R32).</summary>
public class OperatorResetBootstrapPasswordIntegrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string Reason = "Bootstrap password lost; break-glass per runbook.";

    private readonly PostgresTestDatabaseFixture _fixture;

    public OperatorResetBootstrapPasswordIntegrationTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ResetBootstrapCommand_ReplacesThePasswordRevokesSessionsAndAuditsAtomically_ThenSupportsARepeat()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = Slice5Support.BuildProvider(_fixture, logs: logs);
        var admin = await OperatorCommandSeeding.SeedBootstrapAdminAsync(_fixture, provider);
        var hasher = provider.GetRequiredService<IPasswordHasher>();

        var first = await Slice5Support.RunCommandAsync(provider, "reset-bootstrap-password", "--reason", Reason);

        Assert.Equal(0, first.ExitCode);
        var firstPassword = Slice5Support.NewPasswordIn(first.Output);
        var firstCorrelation = Slice5Support.CorrelationIdIn(first.Output);
        Assert.NotEmpty(firstPassword);

        await using (var read = _fixture.CreateDbContext())
        {
            var user = await read.Users.SingleAsync(u => u.Id == admin.UserId);
            Assert.True(user.IsBootstrapAdmin);
            Assert.False(user.MustChangePassword);                       // R19
            Assert.Equal(admin.SecurityVersion + 1, user.SecurityVersion);
            Assert.NotEqual(PasswordVerificationResult.Failed, hasher.Verify(user.PasswordHash, firstPassword));
            Assert.Equal(PasswordVerificationResult.Failed, hasher.Verify(user.PasswordHash, "Original-Password-1"));
            Assert.All(await read.AuthSessions.Where(s => s.UserId == admin.UserId).ToListAsync(), s => Assert.NotNull(s.RevokedAt));

            var auditEvent = await read.AuditEvents.SingleAsync(e => e.EntityId == admin.UserId);
            Assert.Equal(AuditActionType.BootstrapPasswordReset, auditEvent.ActionType);
            Assert.Equal(AuditActorType.System, auditEvent.ActorType);
            Assert.Null(auditEvent.ActorUserId);
            Assert.Equal(AuditSource.SystemJob, auditEvent.Source);
            Assert.Equal(Reason, auditEvent.Reason);
            Assert.Equal(firstCorrelation, auditEvent.CorrelationId);
            Assert.Equal($"BootstrapPasswordReset:command:{firstCorrelation}", auditEvent.IdempotencyKey);
            Assert.Null(auditEvent.Snapshot);
        }

        Assert.DoesNotContain(logs.Entries, e => e.Text.Contains(firstPassword));

        var second = await Slice5Support.RunCommandAsync(provider, "reset-bootstrap-password", "--reason", Reason);
        Assert.Equal(0, second.ExitCode);
        var secondPassword = Slice5Support.NewPasswordIn(second.Output);
        Assert.NotEqual(firstPassword, secondPassword);

        await using var after = _fixture.CreateDbContext();
        var final = await after.Users.SingleAsync(u => u.Id == admin.UserId);
        Assert.NotEqual(PasswordVerificationResult.Failed, hasher.Verify(final.PasswordHash, secondPassword));
        Assert.Equal(2, await after.AuditEvents.CountAsync(e => e.EntityId == admin.UserId));
    }
}

public class OperatorResetBootstrapPasswordFailureIntegrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string Reason = "Bootstrap password lost; break-glass per runbook.";

    private readonly PostgresTestDatabaseFixture _fixture;

    public OperatorResetBootstrapPasswordFailureIntegrationTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task WithNoBootstrapAdmin_TheCommandRefusesAndChangesNothing_ThenWhenTheAuditSaveFailsEverythingRollsBack()
    {
        await using var healthy = Slice5Support.BuildProvider(_fixture);
        var other = await OperatorCommandSeeding.SeedUserAsync(_fixture, healthy);

        var refused = await Slice5Support.RunCommandAsync(healthy, "reset-bootstrap-password", "--reason", Reason);
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("users.not_found", refused.Error);
        await using (var read = _fixture.CreateDbContext())
        {
            Assert.Equal(other.PasswordHash, (await read.Users.SingleAsync(u => u.Id == other.UserId)).PasswordHash);
            Assert.False(await read.AuditEvents.AnyAsync());
        }

        var admin = await OperatorCommandSeeding.SeedBootstrapAdminAsync(_fixture, healthy);
        await using var failing = Slice5Support.BuildProvider(_fixture, failingAuditWriter: true);

        var failed = await Slice5Support.RunCommandAsync(failing, "reset-bootstrap-password", "--reason", Reason);

        Assert.Equal(1, failed.ExitCode);
        Assert.DoesNotContain("shown once", failed.Output);
        await using var after = _fixture.CreateDbContext();
        var user = await after.Users.SingleAsync(u => u.Id == admin.UserId);
        Assert.Equal(admin.PasswordHash, user.PasswordHash);
        Assert.Equal(admin.SecurityVersion, user.SecurityVersion);
        Assert.All(await after.AuthSessions.Where(s => s.UserId == admin.UserId).ToListAsync(), s => Assert.Null(s.RevokedAt));
        Assert.False(await after.AuditEvents.AnyAsync());
    }
}

/// <summary>
/// clear-throttle against PostgreSQL 18 (contract §22 M5, pending review with
/// OC-8): the throttle-row delete (<see cref="IThrottleIdentifierEraser"/>) and
/// the ThrottleCleared audit event now run in one transaction (ADR 0006 §6),
/// so they must be proved to commit or roll back together - not merely that
/// the use case reports a consistent result, which the fake-based
/// Application.Tests already cover.
///
/// PENDING: not executed in the 2026-10-19 remediation pass (requires
/// CONTRACTRA_TEST_POSTGRES against an ephemeral PostgreSQL 18 database). To
/// run safely: set CONTRACTRA_TEST_POSTGRES to a disposable connection string
/// and run `dotnet test Contractra.Infrastructure.IntegrationTests` in
/// isolation, with nothing else connected to that instance.
/// </summary>
public class ClearThrottleIntegrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string Reason = "Legitimate user starved by a coordinated distributed attack.";

    private static readonly DateTimeOffset T0 = AuthThrottle.ToStoredPrecision(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));

    private readonly PostgresTestDatabaseFixture _fixture;

    public ClearThrottleIntegrationTests(PostgresTestDatabaseFixture fixture) => _fixture = fixture;

    private static byte[] NewIdentifierKey() => ThrottleKeys.IdentifierKey($"eng.{Guid.NewGuid():N}");

    /// <summary>Puts the identifier into layer 3 slow mode, so its row exists before the command runs.</summary>
    private static async Task EnterSlowModeAsync(ServiceProvider provider, byte[] identifierKey)
    {
        var throttle = provider.GetRequiredService<IAuthThrottle>();
        for (var i = 0; i < 11; i++)
        {
            await throttle.AdmitCredentialAttemptAsync(identifierKey, $"203.0.113.{i}", T0.AddSeconds(i));
        }
    }

    [Fact]
    public async Task ClearThrottleCommand_DeletesTheIdentifierRow_AndAuditsInOneCommit()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = Slice5Support.BuildProvider(_fixture, logs: logs, withAuthThrottle: true);
        var identifier = $"eng.{Guid.NewGuid():N}";
        var identifierKey = ThrottleKeys.IdentifierKey(identifier);
        await EnterSlowModeAsync(provider, identifierKey);

        // Seeding goes through the login-path throttle, whose §17 logging carries
        // the identifier key; only what the command itself emits is checked below.
        logs.Entries.Clear();

        var (exit, output, error) = await Slice5Support.RunCommandAsync(
            provider, "clear-throttle", "--identifier", identifier, "--reason", Reason);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error);
        Assert.Contains("Throttle cleared", output);
        var correlationId = Slice5Support.CorrelationIdIn(output);
        Assert.NotEmpty(correlationId);

        var keyHex = ThrottleKeys.ToHex(identifierKey);
        Assert.DoesNotContain(keyHex, output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(identifier, output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(logs.Entries, e => e.Text.Contains(keyHex, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs.Entries, e => e.Text.Contains(identifier, StringComparison.OrdinalIgnoreCase));

        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<AuthThrottleStore>();
        Assert.Null(await store.GetAsync(new AuthThrottleKey(AuthThrottleScopes.Identifier, identifierKey)));

        await using var read = _fixture.CreateDbContext();
        var expectedEntityId = Guid.Parse(Slice5Support.CorrelationIdIn(output));
        var auditEvent = await read.AuditEvents.SingleAsync(e => e.EntityId == expectedEntityId);
        Assert.Equal("OperatorCommandRun", auditEvent.EntityType);
        Assert.Equal(AuditActionType.ThrottleCleared, auditEvent.ActionType);
        Assert.Equal(AuditActorType.System, auditEvent.ActorType);
        Assert.Null(auditEvent.ActorUserId);
        Assert.Null(auditEvent.ActorRoleCode);
        Assert.Equal(AuditSource.SystemJob, auditEvent.Source);
        Assert.Equal(Reason, auditEvent.Reason);
        Assert.Equal(correlationId, auditEvent.CorrelationId);
        Assert.Equal($"ThrottleCleared:command:{correlationId}", auditEvent.IdempotencyKey);
        Assert.DoesNotContain(identifier, auditEvent.Reason!);
    }

    [Fact]
    public async Task ClearThrottleCommand_WhenTheAuditSaveFails_RollsBackTheDelete_LeavingTheRowIntact()
    {
        // The core atomicity proof (ADR 0006 §6): before this redesign, the
        // delete ran in AuthThrottleStore's own short transaction and would
        // have already committed by the time the audit write below fails. Now
        // both share the use case's transaction, so the row must still exist
        // after the command reports failure.
        await using var provider = Slice5Support.BuildProvider(_fixture, failingAuditWriter: true, withAuthThrottle: true);
        var identifier = $"eng.{Guid.NewGuid():N}";
        var identifierKey = ThrottleKeys.IdentifierKey(identifier);
        await EnterSlowModeAsync(provider, identifierKey);

        var (exit, output, error) = await Slice5Support.RunCommandAsync(
            provider, "clear-throttle", "--identifier", identifier, "--reason", Reason);

        Assert.Equal(1, exit);
        Assert.DoesNotContain("Throttle cleared", output);
        Assert.Contains("Nothing was changed", error);

        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<AuthThrottleStore>();
        var row = await store.GetAsync(new AuthThrottleKey(AuthThrottleScopes.Identifier, identifierKey));
        Assert.NotNull(row);

        await using var read = _fixture.CreateDbContext();
        var expectedEntityId = Guid.Parse(Slice5Support.CorrelationIdIn(output));
        Assert.False(await read.AuditEvents.AnyAsync(e => e.EntityId == expectedEntityId));
    }

    [Fact]
    public async Task ClearThrottleCommand_WhenNoRowExists_StillSucceedsAndAudits()
    {
        await using var provider = Slice5Support.BuildProvider(_fixture);
        var identifier = $"eng.{Guid.NewGuid():N}";
        var identifierKey = ThrottleKeys.IdentifierKey(identifier);

        var (exit, output, error) = await Slice5Support.RunCommandAsync(
            provider, "clear-throttle", "--identifier", identifier, "--reason", Reason);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error);
        Assert.Contains("Throttle cleared", output);

        await using var read = _fixture.CreateDbContext();
        var expectedEntityId = Guid.Parse(Slice5Support.CorrelationIdIn(output));
        Assert.True(await read.AuditEvents.AnyAsync(e => e.EntityId == expectedEntityId));
    }

    [Fact]
    public async Task ClearThrottleCommand_DoesNotTouchLayer2PairsOrOtherIdentifiers()
    {
        await using var provider = Slice5Support.BuildProvider(_fixture, withAuthThrottle: true);
        var throttle = provider.GetRequiredService<IAuthThrottle>();
        var cleared = $"eng.{Guid.NewGuid():N}";
        var clearedKey = ThrottleKeys.IdentifierKey(cleared);
        var otherKey = NewIdentifierKey();
        await EnterSlowModeAsync(provider, clearedKey);
        await throttle.AdmitCredentialAttemptAsync(otherKey, "203.0.113.200", T0);

        await Slice5Support.RunCommandAsync(provider, "clear-throttle", "--identifier", cleared, "--reason", Reason);

        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<AuthThrottleStore>();
        Assert.NotNull(await store.GetAsync(new AuthThrottleKey(AuthThrottleScopes.LoginPair, AuthThrottle.PairKey(clearedKey, "203.0.113.0"))));
        Assert.NotNull(await store.GetAsync(new AuthThrottleKey(AuthThrottleScopes.Identifier, otherKey)));
    }
}
