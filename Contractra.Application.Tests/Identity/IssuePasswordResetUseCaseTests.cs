using Contractra.Application.Identity;
using Contractra.Application.Identity.UseCases;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace Contractra.Application.Tests.Identity;

/// <summary>Auth contract v1.5 §9.6, §10A, owner decisions R15, R20, R27, R43, R44.</summary>
public class IssuePasswordResetUseCaseTests
{
    private static readonly Guid ActorId = Guid.CreateVersion7();

    private readonly FakeUserRepository _users = new();
    private readonly FakeSessionStore _sessions = new();
    private readonly FakePasswordResetTicketStore _tickets = new();
    private readonly FakeResetCodeGenerator _codes = new();
    private readonly FakeAuditWriter _auditWriter = new();
    private readonly FakeClock _clock = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private IssuePasswordResetUseCase CreateUseCase(params IPasswordResetDelivery[] channels) => new(
        _users, _sessions, _tickets, _codes, _auditWriter, _clock, _unitOfWork, channels);

    private IssuePasswordResetUseCase CreateUseCaseWithLogger(
        ListLogger<IssuePasswordResetUseCase> logger, params IPasswordResetDelivery[] channels) => new(
        _users, _sessions, _tickets, _codes, _auditWriter, _clock, _unitOfWork, channels, codeLifetime: null, logger);

    private User SeedUserWithChosenPassword(string login = "eng.ahmed")
    {
        var user = new User(new LoginIdentifier(login), UserStatus.Active, "realhash", mustChangePassword: false);
        _users.Seed(user);
        return user;
    }

    [Fact]
    public async Task IssueReset_WithAReachableChannel_InvalidatesPasswordAndIssuesATicket()
    {
        var user = SeedUserWithChosenPassword();
        var channel = new FakePasswordResetDelivery("operator");
        var useCase = CreateUseCase(channel);

        var result = await useCase.ExecuteAsync(new IssuePasswordResetRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.Equal("operator", result.Value!.Channel);
        Assert.False(user.HasUsablePassword);
        Assert.True(_unitOfWork.Committed);

        var ticket = await _tickets.FindOpenAsync(user.Id);
        Assert.NotNull(ticket);
        Assert.Equal(ActorId, ticket!.IssuedByUserId);
    }

    [Fact]
    public async Task IssueReset_SendsTheCodeOnlyAfterTheTransactionCommits()
    {
        var user = SeedUserWithChosenPassword();
        var channel = new FakePasswordResetDelivery("operator");
        bool? committedWhenSent = null;
        int? stagedWhenSent = null;
        channel.OnSend = () =>
        {
            committedWhenSent = _unitOfWork.Committed;
            stagedWhenSent = _auditWriter.Staged.Count;
        };
        var useCase = CreateUseCase(channel);

        var result = await useCase.ExecuteAsync(new IssuePasswordResetRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.DeliveryFailed);
        var sent = Assert.Single(channel.Sent);
        Assert.Equal(user.Id, sent.UserId);
        Assert.NotEmpty(sent.Code);
        Assert.True(committedWhenSent, "The code must be handed to the channel only after the commit.");
        Assert.Equal(1, stagedWhenSent);
    }

    [Fact]
    public async Task IssueReset_WhenDeliveryThrows_KeepsTheCommittedTicketLogsAndReportsTheFailure()
    {
        var user = SeedUserWithChosenPassword();
        var channel = new FakePasswordResetDelivery("operator") { ThrowOnSend = new InvalidOperationException("channel down") };
        var logger = new ListLogger<IssuePasswordResetUseCase>();
        var useCase = CreateUseCaseWithLogger(logger, channel);

        var result = await useCase.ExecuteAsync(new IssuePasswordResetRequest(user.Id, ActorId, "corr-deliver", AuditSource.Web));

        // Not a pretend success, not a rollback: the use case reports what happened.
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.DeliveryFailed);
        Assert.Equal("operator", result.Value.Channel);

        // §9.6 step 6: the committed work and the ticket stand until they expire.
        Assert.True(_unitOfWork.Committed);
        Assert.False(user.HasUsablePassword);
        Assert.Equal(SessionRevokeReason.PasswordResetIssued, Assert.Single(_sessions.RevokeAllCalls).Reason);
        var ticket = await _tickets.FindOpenAsync(user.Id);
        Assert.NotNull(ticket);
        Assert.True(ticket!.IsValidAt(_clock.UtcNow));
        Assert.True(ticket.IsValidAt(result.Value.ExpiresAt.AddSeconds(-1)));
        Assert.Equal(AuditActionType.PasswordResetIssued, Assert.Single(_auditWriter.Staged).ActionType);

        // §14: a delivery failure is logged with the correlation id - never the code.
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("corr-deliver", entry.Message);
        Assert.Contains(ticket.Id.ToString(), entry.Message);
        Assert.Contains(user.Id.ToString(), entry.Message);
        Assert.Null(entry.Exception);
        var attemptedCode = Assert.Single(channel.Sent).Code;
        Assert.DoesNotContain(attemptedCode, entry.Message);
        Assert.DoesNotContain(Convert.ToHexString(ticket.CodeHash), entry.Message);
    }

    [Fact]
    public async Task IssueReset_AfterAFailedDelivery_ANewResetSupersedesTheStillValidTicket()
    {
        var user = SeedUserWithChosenPassword();
        var failing = new FakePasswordResetDelivery("operator") { ThrowOnSend = new InvalidOperationException("channel down") };
        var first = await CreateUseCase(failing).ExecuteAsync(
            new IssuePasswordResetRequest(user.Id, ActorId, "corr-1", AuditSource.Web));
        var firstTicket = await _tickets.FindOpenAsync(user.Id);

        _unitOfWork.Reset();
        var working = new FakePasswordResetDelivery("operator");
        var second = await CreateUseCase(working).ExecuteAsync(
            new IssuePasswordResetRequest(user.Id, ActorId, "corr-2", AuditSource.Web));

        Assert.True(first.Value!.DeliveryFailed);
        Assert.False(second.Value!.DeliveryFailed);
        Assert.NotNull(firstTicket);
        Assert.False(firstTicket!.IsOpen);
        var current = await _tickets.FindOpenAsync(user.Id);
        Assert.NotNull(current);
        Assert.NotEqual(firstTicket.Id, current!.Id);
    }

    [Fact]
    public async Task IssueReset_WhenDeliverySucceeds_LogsNothing()
    {
        var user = SeedUserWithChosenPassword();
        var logger = new ListLogger<IssuePasswordResetUseCase>();
        var useCase = CreateUseCaseWithLogger(logger, new FakePasswordResetDelivery("operator"));

        await useCase.ExecuteAsync(new IssuePasswordResetRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task IssueReset_AuditEvent_CarriesActorTargetSourceCorrelationAndTheTicketSnapshot()
    {
        var user = SeedUserWithChosenPassword();
        var channel = new FakePasswordResetDelivery("operator");
        var useCase = CreateUseCase(channel);

        var result = await useCase.ExecuteAsync(new IssuePasswordResetRequest(user.Id, ActorId, "corr-issue", AuditSource.Web));

        var ticket = await _tickets.FindOpenAsync(user.Id);
        var auditEvent = Assert.Single(_auditWriter.Staged);
        AuditAssert.Event(auditEvent, AuditActionType.PasswordResetIssued, ActorId, user.Id, "corr-issue", AuditSource.Web, _clock.UtcNow);
        AuditAssert.SnapshotKeys(auditEvent, "password_reset_ticket_id", "issued_by_user_id", "channel", "issued_at", "expires_at", "consumed_at");
        var snapshot = AuditAssert.SnapshotOf(auditEvent);
        Assert.Equal(ticket!.Id.ToString(), snapshot["password_reset_ticket_id"]);
        Assert.Equal(ActorId.ToString(), snapshot["issued_by_user_id"]);
        Assert.Equal("operator", snapshot["channel"]);
        AuditAssert.SnapshotTime(snapshot, "issued_at", _clock.UtcNow);
        AuditAssert.SnapshotTime(snapshot, "expires_at", result.Value!.ExpiresAt);
        Assert.Null(snapshot["consumed_at"]);
        AuditAssert.ContainsNone(auditEvent, channel.Sent.Single().Code, Convert.ToHexString(ticket.CodeHash), user.PasswordHash);
    }

    [Fact]
    public async Task IssueReset_RevokesAllSessionsAndStagesPasswordResetIssuedAudit()
    {
        var user = SeedUserWithChosenPassword();
        var useCase = CreateUseCase(new FakePasswordResetDelivery());

        await useCase.ExecuteAsync(new IssuePasswordResetRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        var call = Assert.Single(_sessions.RevokeAllCalls);
        Assert.Equal(user.Id, call.UserId);
        Assert.Equal(SessionRevokeReason.PasswordResetIssued, call.Reason);
        Assert.Null(call.ExceptSessionId);

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal(AuditActionType.PasswordResetIssued, auditEvent.ActionType);
        Assert.Equal(ActorId, auditEvent.ActorUserId);
    }

    [Fact]
    public async Task IssueReset_WithNoReachableChannel_FailsClosedAndChangesNothing()
    {
        var user = SeedUserWithChosenPassword();
        var hashBefore = user.PasswordHash;
        var useCase = CreateUseCase(new FakePasswordResetDelivery(canReach: false));

        var result = await useCase.ExecuteAsync(new IssuePasswordResetRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.reset_delivery_unavailable", result.Error!.Code);
        Assert.Equal(hashBefore, user.PasswordHash);
        Assert.False(_unitOfWork.TransactionOpen);
        Assert.Empty(_auditWriter.Staged);
    }

    [Fact]
    public async Task IssueReset_WithNoChannelRegisteredAtAll_FailsClosed()
    {
        var user = SeedUserWithChosenPassword();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new IssuePasswordResetRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.reset_delivery_unavailable", result.Error!.Code);
    }

    [Fact]
    public async Task IssueReset_AnUnknownUser_Fails()
    {
        var useCase = CreateUseCase(new FakePasswordResetDelivery());

        var result = await useCase.ExecuteAsync(new IssuePasswordResetRequest(Guid.CreateVersion7(), ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.not_found", result.Error!.Code);
    }

    [Fact]
    public async Task IssueReset_ADisabledUser_Fails()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Disabled, "hash", mustChangePassword: false);
        _users.Seed(user);
        var useCase = CreateUseCase(new FakePasswordResetDelivery());

        var result = await useCase.ExecuteAsync(new IssuePasswordResetRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.already_disabled", result.Error!.Code);
    }

    [Fact]
    public async Task IssueReset_AUserStillOnItsFirstTemporaryPassword_Fails()
    {
        var user = new User(
            new LoginIdentifier("eng.ahmed"), UserStatus.Active, "hash", mustChangePassword: true, _clock.UtcNow.AddHours(72));
        _users.Seed(user);
        var useCase = CreateUseCase(new FakePasswordResetDelivery());

        var result = await useCase.ExecuteAsync(new IssuePasswordResetRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.initial_password_pending", result.Error!.Code);
    }

    [Fact]
    public async Task IssueReset_TheBootstrapAdmin_IsProtected()
    {
        var adminRole = new Role(new RoleCode("Admin"), RoleStatus.Active);
        var payrollView = new Permission(new PermissionCode(PermissionCode.PayrollView), PermissionStatus.Active);
        var admin = User.CreateBootstrapAdmin(
            new LoginIdentifier("bootstrap.admin"), "hash", adminRole, payrollView, _clock.UtcNow, _clock.UtcNow);
        _users.Seed(admin);
        var useCase = CreateUseCase(new FakePasswordResetDelivery());

        var result = await useCase.ExecuteAsync(new IssuePasswordResetRequest(admin.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.bootstrap_account_protected", result.Error!.Code);
    }
}
