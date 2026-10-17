using Contractra.Application.Identity;
using Contractra.Application.Identity.Operations;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Contractra.Application.Tests.Identity.Operations;

/// <summary>
/// The two operator commands: auth contract v1.5 §10A (interim operator
/// procedure), §11 (lost bootstrap password), §13, §14, §18; owner decisions
/// R15, R19, R26, R29, R32, R44; repository ADR 0006 §5.3, E34.
/// </summary>
public class OperatorCommandUseCaseTests
{
    private const string Reason = "User lost the device holding the password manager.";

    private readonly FakeUserRepository _users = new();
    private readonly FakeSessionStore _sessions = new();
    private readonly FakePasswordResetTicketStore _tickets = new();
    private readonly FakeResetCodeGenerator _codes = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeTemporaryPasswordGenerator _passwords = new();
    private readonly FakeAuditWriter _auditWriter = new();
    private readonly FakeClock _clock = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly ListLogger<OperatorIssuePasswordResetUseCase> _issueLogger = new();
    private readonly ListLogger<ResetBootstrapPasswordUseCase> _resetLogger = new();

    private OperatorIssuePasswordResetUseCase CreateIssue() =>
        new(_users, _sessions, _tickets, _codes, _auditWriter, _clock, _unitOfWork, logger: _issueLogger);

    private ResetBootstrapPasswordUseCase CreateResetBootstrap() =>
        new(_users, _sessions, _hasher, _passwords, _auditWriter, _clock, _unitOfWork, _resetLogger);

    private User SeedUserWithChosenPassword(string login = "eng.ahmed")
    {
        var user = new User(new LoginIdentifier(login), UserStatus.Active, "realhash", mustChangePassword: false);
        _users.Seed(user);
        return user;
    }

    private User SeedBootstrapAdmin()
    {
        var admin = User.CreateBootstrapAdmin(
            new LoginIdentifier("bootstrap.admin"), _hasher.Hash("Original-Password-1"),
            new Role(new RoleCode("Admin"), RoleStatus.Active),
            new Permission(new PermissionCode(PermissionCode.PayrollView), PermissionStatus.Active),
            _clock.UtcNow, _clock.UtcNow);
        _users.Seed(admin);
        return admin;
    }

    // ---- issue-password-reset ---------------------------------------------

    [Fact]
    public async Task IssueReset_InvalidatesThePassword_IssuesAnOperatorTicket_RevokesSessions_AndReturnsTheCodeOnce()
    {
        var user = SeedUserWithChosenPassword();
        var versionBefore = user.SecurityVersion;

        var result = await CreateIssue().ExecuteAsync(new OperatorIssuePasswordResetRequest("ENG.AHMED", Reason, "run-1"));

        Assert.True(result.IsSuccess);
        Assert.False(user.HasUsablePassword);
        Assert.Equal(versionBefore + 1, user.SecurityVersion);
        Assert.True(_unitOfWork.Committed);

        var ticket = await _tickets.FindOpenAsync(user.Id);
        Assert.NotNull(ticket);
        Assert.Equal("operator", ticket!.Channel);
        Assert.Null(ticket.IssuedByUserId);
        Assert.Equal(_clock.UtcNow.AddHours(24), ticket.ExpiresAt);
        Assert.Equal(_codes.Hash(result.Value!.Code), ticket.CodeHash);
        Assert.Equal(ticket.Id, result.Value.TicketId);

        var call = Assert.Single(_sessions.RevokeAllCalls);
        Assert.Equal(user.Id, call.UserId);
        Assert.Equal(SessionRevokeReason.PasswordResetIssued, call.Reason);
        Assert.Null(call.ExceptSessionId);
    }

    [Fact]
    public async Task IssueReset_Audit_IsASystemSystemJobEventWithTheReasonTheRunCorrelationAndTheCommandKey()
    {
        var user = SeedUserWithChosenPassword();

        var result = await CreateIssue().ExecuteAsync(new OperatorIssuePasswordResetRequest(user.LoginIdentifier.Value, Reason, "run-42"));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal(AuditActionType.PasswordResetIssued, auditEvent.ActionType);
        Assert.Equal(AuditActorType.System, auditEvent.ActorType);
        Assert.Null(auditEvent.ActorUserId);
        Assert.Null(auditEvent.ActorRoleCode);
        Assert.Equal(AuditSource.SystemJob, auditEvent.Source);
        Assert.Equal("User", auditEvent.EntityType);
        Assert.Equal(user.Id, auditEvent.EntityId);
        Assert.Equal("run-42", auditEvent.CorrelationId);
        Assert.Equal(Reason, auditEvent.Reason);
        Assert.Equal("PasswordResetIssued:command:run-42", auditEvent.IdempotencyKey);
        Assert.Equal(_clock.UtcNow, auditEvent.OccurredAtUtc);

        AuditAssert.SnapshotKeys(auditEvent, "password_reset_ticket_id", "issued_by_user_id", "channel", "issued_at", "expires_at", "consumed_at");
        var snapshot = AuditAssert.SnapshotOf(auditEvent);
        Assert.Equal(result.Value!.TicketId.ToString(), snapshot["password_reset_ticket_id"]);
        Assert.Null(snapshot["issued_by_user_id"]);
        Assert.Equal("operator", snapshot["channel"]);
        Assert.Null(snapshot["consumed_at"]);
        AuditAssert.ContainsNone(auditEvent, result.Value.Code, "realhash", Convert.ToHexString(_codes.Hash(result.Value.Code)));
    }

    [Fact]
    public async Task IssueReset_NeverLogsTheCode()
    {
        var user = SeedUserWithChosenPassword();

        var result = await CreateIssue().ExecuteAsync(new OperatorIssuePasswordResetRequest(user.LoginIdentifier.Value, Reason, "run-1"));

        Assert.NotEmpty(_issueLogger.Entries);
        Assert.All(_issueLogger.Entries, entry => Assert.DoesNotContain(result.Value!.Code, entry.Message));
    }

    [Fact]
    public async Task IssueReset_Repeated_IssuesANewTicketThatSupersedesTheFirst_AndAuditsBothRunsUnderTheirOwnKeys()
    {
        var user = SeedUserWithChosenPassword();
        var first = await CreateIssue().ExecuteAsync(new OperatorIssuePasswordResetRequest(user.LoginIdentifier.Value, Reason, "run-1"));
        var firstTicket = await _tickets.FindOpenAsync(user.Id);
        _unitOfWork.Reset();

        var second = await CreateIssue().ExecuteAsync(new OperatorIssuePasswordResetRequest(user.LoginIdentifier.Value, Reason, "run-2"));

        Assert.True(second.IsSuccess);
        Assert.False(firstTicket!.IsOpen);
        var open = await _tickets.FindOpenAsync(user.Id);
        Assert.Equal(second.Value!.TicketId, open!.Id);
        Assert.NotEqual(first.Value!.TicketId, second.Value.TicketId);
        Assert.Equal(
            new[] { "PasswordResetIssued:command:run-1", "PasswordResetIssued:command:run-2" },
            _auditWriter.Staged.Select(e => e.IdempotencyKey));
    }

    [Fact]
    public async Task IssueReset_RefusesTheBootstrapAdmin_AndChangesAndAuditsNothing()
    {
        var admin = SeedBootstrapAdmin();
        var hashBefore = admin.PasswordHash;

        var result = await CreateIssue().ExecuteAsync(new OperatorIssuePasswordResetRequest(admin.LoginIdentifier.Value, Reason, "run-1"));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.bootstrap_account_protected", result.Error!.Code);
        AssertNothingChanged(admin, hashBefore);
    }

    [Fact]
    public async Task IssueReset_RefusesADisabledUser()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Disabled, "realhash", mustChangePassword: false);
        _users.Seed(user);

        var result = await CreateIssue().ExecuteAsync(new OperatorIssuePasswordResetRequest("eng.ahmed", Reason, "run-1"));

        Assert.Equal("users.already_disabled", result.Error!.Code);
        AssertNothingChanged(user, "realhash");
    }

    [Fact]
    public async Task IssueReset_RefusesAnAccountStillOnItsFirstTemporaryPassword()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Active, "temp", mustChangePassword: true, _clock.UtcNow.AddHours(48));
        _users.Seed(user);

        var result = await CreateIssue().ExecuteAsync(new OperatorIssuePasswordResetRequest("eng.ahmed", Reason, "run-1"));

        Assert.Equal("users.initial_password_pending", result.Error!.Code);
        AssertNothingChanged(user, "temp");
    }

    [Fact]
    public async Task IssueReset_RefusesAnUnknownLogin()
    {
        var result = await CreateIssue().ExecuteAsync(new OperatorIssuePasswordResetRequest("nobody", Reason, "run-1"));

        Assert.Equal("users.not_found", result.Error!.Code);
        Assert.Empty(_auditWriter.Staged);
        Assert.False(_unitOfWork.TransactionOpen);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" padded")]
    [InlineData("padded ")]
    public async Task IssueReset_RequiresAValidReason_BeforeReadingAnything(string reason)
    {
        var user = SeedUserWithChosenPassword();

        var result = await CreateIssue().ExecuteAsync(new OperatorIssuePasswordResetRequest(user.LoginIdentifier.Value, reason, "run-1"));

        Assert.Equal("validation.failed", result.Error!.Code);
        AssertNothingChanged(user, "realhash");
    }

    [Fact]
    public async Task IssueReset_ReasonLimit_Is1000Characters()
    {
        var user = SeedUserWithChosenPassword();

        var tooLong = await CreateIssue().ExecuteAsync(new OperatorIssuePasswordResetRequest(user.LoginIdentifier.Value, new string('r', 1001), "run-1"));
        var atLimit = await CreateIssue().ExecuteAsync(new OperatorIssuePasswordResetRequest(user.LoginIdentifier.Value, new string('r', 1000), "run-2"));

        Assert.Equal("validation.failed", tooLong.Error!.Code);
        Assert.True(atLimit.IsSuccess);
    }

    // ---- reset-bootstrap-password -----------------------------------------

    [Fact]
    public async Task ResetBootstrap_SetsAFreshPassword_KeepsTheForcedChangeOff_BumpsTheVersion_AndRevokesSessions()
    {
        var admin = SeedBootstrapAdmin();
        var versionBefore = admin.SecurityVersion;
        var hashBefore = admin.PasswordHash;

        var result = await CreateResetBootstrap().ExecuteAsync(new ResetBootstrapPasswordRequest(Reason, "run-1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(admin.Id, result.Value!.UserId);
        Assert.NotEqual(hashBefore, admin.PasswordHash);
        Assert.Equal(_hasher.Hash(result.Value.NewPassword), admin.PasswordHash);
        Assert.False(admin.MustChangePassword);                 // R19
        Assert.True(admin.IsBootstrapAdmin);
        Assert.Equal(versionBefore + 1, admin.SecurityVersion);
        Assert.True(_unitOfWork.Committed);

        var call = Assert.Single(_sessions.RevokeAllCalls);
        Assert.Equal(admin.Id, call.UserId);
        Assert.Null(call.ExceptSessionId);
    }

    [Fact]
    public async Task ResetBootstrap_Audit_IsASystemSystemJobBootstrapPasswordResetWithTheReasonAndTheCommandKey_AndNoSecret()
    {
        var admin = SeedBootstrapAdmin();

        var result = await CreateResetBootstrap().ExecuteAsync(new ResetBootstrapPasswordRequest(Reason, "run-9"));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal(AuditActionType.BootstrapPasswordReset, auditEvent.ActionType);
        Assert.Equal(AuditActorType.System, auditEvent.ActorType);
        Assert.Null(auditEvent.ActorUserId);
        Assert.Null(auditEvent.ActorRoleCode);
        Assert.Equal(AuditSource.SystemJob, auditEvent.Source);
        Assert.Equal(admin.Id, auditEvent.EntityId);
        Assert.Equal("run-9", auditEvent.CorrelationId);
        Assert.Equal(Reason, auditEvent.Reason);
        Assert.Equal("BootstrapPasswordReset:command:run-9", auditEvent.IdempotencyKey);
        Assert.Null(auditEvent.Snapshot);
        AuditAssert.ContainsNone(auditEvent, result.Value!.NewPassword, admin.PasswordHash);
    }

    [Fact]
    public async Task ResetBootstrap_NeverLogsThePassword()
    {
        SeedBootstrapAdmin();

        var result = await CreateResetBootstrap().ExecuteAsync(new ResetBootstrapPasswordRequest(Reason, "run-1"));

        Assert.NotEmpty(_resetLogger.Entries);
        Assert.All(_resetLogger.Entries, entry => Assert.DoesNotContain(result.Value!.NewPassword, entry.Message));
    }

    [Fact]
    public async Task ResetBootstrap_Repeated_GeneratesAnotherPasswordAndAuditsEachRunUnderItsOwnKey()
    {
        var admin = SeedBootstrapAdmin();
        var first = await CreateResetBootstrap().ExecuteAsync(new ResetBootstrapPasswordRequest(Reason, "run-1"));
        _unitOfWork.Reset();

        var second = await CreateResetBootstrap().ExecuteAsync(new ResetBootstrapPasswordRequest(Reason, "run-2"));

        Assert.NotEqual(first.Value!.NewPassword, second.Value!.NewPassword);
        Assert.Equal(_hasher.Hash(second.Value.NewPassword), admin.PasswordHash);
        Assert.Equal(
            new[] { "BootstrapPasswordReset:command:run-1", "BootstrapPasswordReset:command:run-2" },
            _auditWriter.Staged.Select(e => e.IdempotencyKey));
    }

    [Fact]
    public async Task ResetBootstrap_WithNoBootstrapAdmin_RefusesAndTouchesNobodyElse()
    {
        var other = SeedUserWithChosenPassword();

        var result = await CreateResetBootstrap().ExecuteAsync(new ResetBootstrapPasswordRequest(Reason, "run-1"));

        Assert.Equal("users.not_found", result.Error!.Code);
        AssertNothingChanged(other, "realhash");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" padded")]
    public async Task ResetBootstrap_RequiresAValidReason(string reason)
    {
        var admin = SeedBootstrapAdmin();
        var hashBefore = admin.PasswordHash;

        var result = await CreateResetBootstrap().ExecuteAsync(new ResetBootstrapPasswordRequest(reason, "run-1"));

        Assert.Equal("validation.failed", result.Error!.Code);
        AssertNothingChanged(admin, hashBefore);
    }

    private void AssertNothingChanged(User user, string expectedHash)
    {
        Assert.Equal(expectedHash, user.PasswordHash);
        Assert.Empty(_auditWriter.Staged);
        Assert.Empty(_sessions.RevokeAllCalls);
        Assert.False(_unitOfWork.TransactionOpen);
        Assert.False(_unitOfWork.Committed);
    }
}
