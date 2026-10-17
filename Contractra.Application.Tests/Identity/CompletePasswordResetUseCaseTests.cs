using Contractra.Application.Identity.UseCases;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity;

/// <summary>Auth contract v1.5 §5.6, owner decision R44.</summary>
public class CompletePasswordResetUseCaseTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeSessionStore _sessions = new();
    private readonly FakePasswordResetTicketStore _tickets = new();
    private readonly FakeResetCodeGenerator _codes = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeAuditWriter _auditWriter = new();
    private readonly FakeClock _clock = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private CompletePasswordResetUseCase CreateUseCase() =>
        new(_users, _sessions, _tickets, _codes, _hasher, _auditWriter, _clock, _unitOfWork);

    private (User User, PasswordResetTicket Ticket, string PlainCode) SeedUserWithOpenTicket(string login = "eng.ahmed")
    {
        var user = new User(new LoginIdentifier(login), UserStatus.Active, "!", mustChangePassword: false);
        _users.Seed(user);
        var generated = _codes.Generate();
        var ticket = PasswordResetTicket.Issue(user.Id, generated.Hash, _clock.UtcNow, _clock.UtcNow.AddHours(24), null, "operator");
        _tickets.Seed(ticket);
        return (user, ticket, generated.Code);
    }

    [Fact]
    public async Task Complete_WithTheCorrectCode_SetsNewPasswordConsumesTicketAndAudits()
    {
        var (user, ticket, code) = SeedUserWithOpenTicket();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            user.LoginIdentifier.Value, code, "NewPassw0rd", "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.Equal(_hasher.Hash("NewPassw0rd"), user.PasswordHash);
        Assert.False(user.MustChangePassword);
        Assert.True(ticket.ConsumedAt.HasValue);
        Assert.True(_unitOfWork.Committed);

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal(AuditActionType.PasswordResetCompleted, auditEvent.ActionType);
        Assert.Equal(user.Id, auditEvent.ActorUserId);
    }

    [Fact]
    public async Task Complete_RevokesAllSessions()
    {
        var (user, _, code) = SeedUserWithOpenTicket();
        var session = AuthSession.Start(user.Id, ClientApp.Employee, ClientPlatform.Mobile, new byte[32], _clock.UtcNow, _clock.UtcNow.AddDays(7));
        _sessions.Seed(session);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            user.LoginIdentifier.Value, code, "NewPassw0rd", "corr-1", AuditSource.Web));

        var call = Assert.Single(_sessions.RevokeAllCalls);
        Assert.Equal(user.Id, call.UserId);
        Assert.Equal(SessionRevokeReason.PasswordResetCompleted, call.Reason);
        Assert.Null(call.ExceptSessionId);
    }

    [Fact]
    public async Task Complete_WithAnUnknownIdentifier_FailsWithTheGenericError()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            "no.such.user", "CODE-0001-0000-0000", "NewPassw0rd", "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.reset_code_invalid", result.Error!.Code);
        Assert.False(_unitOfWork.TransactionOpen);
        Assert.Empty(_auditWriter.Staged);
    }

    [Fact]
    public async Task Complete_ForADisabledUser_FailsWithTheGenericError()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Disabled, "!", mustChangePassword: false);
        _users.Seed(user);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            "eng.ahmed", "CODE-0001-0000-0000", "NewPassw0rd", "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.reset_code_invalid", result.Error!.Code);
    }

    [Fact]
    public async Task Complete_WithNoOpenTicket_FailsWithTheGenericError()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Active, "!", mustChangePassword: false);
        _users.Seed(user);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            "eng.ahmed", "CODE-0001-0000-0000", "NewPassw0rd", "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.reset_code_invalid", result.Error!.Code);
    }

    [Fact]
    public async Task Complete_WithTheWrongCode_FailsAndChangesNothing()
    {
        var (user, ticket, _) = SeedUserWithOpenTicket();
        var hashBefore = user.PasswordHash;
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            user.LoginIdentifier.Value, "WRNG-0000-0000-0000", "NewPassw0rd", "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.reset_code_invalid", result.Error!.Code);
        Assert.Equal(hashBefore, user.PasswordHash);
        Assert.True(ticket.IsOpen);
    }

    [Fact]
    public async Task Complete_WithAnExpiredCode_FailsWithTheGenericError()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Active, "!", mustChangePassword: false);
        _users.Seed(user);
        var generated = _codes.Generate();
        var ticket = PasswordResetTicket.Issue(user.Id, generated.Hash, _clock.UtcNow.AddHours(-30), _clock.UtcNow.AddHours(-6), null, "operator");
        _tickets.Seed(ticket);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            "eng.ahmed", generated.Code, "NewPassw0rd", "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.reset_code_invalid", result.Error!.Code);
        Assert.True(ticket.IsOpen);
    }

    [Theory]
    [InlineData("Sh0rt1")]
    public async Task Complete_WithATooShortNewPassword_Fails(string tooShort)
    {
        var (user, _, code) = SeedUserWithOpenTicket();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            user.LoginIdentifier.Value, code, tooShort, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.password_too_short", result.Error!.Code);
    }

    [Fact]
    public async Task Complete_AuditEvent_NamesTheUserAsActorAndSnapshotsTheConsumedTicket()
    {
        var (user, ticket, code) = SeedUserWithOpenTicket();
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            user.LoginIdentifier.Value, code, "NewPassw0rd", "corr-complete", AuditSource.EmployeeApp));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        AuditAssert.Event(auditEvent, AuditActionType.PasswordResetCompleted, user.Id, user.Id, "corr-complete", AuditSource.EmployeeApp, _clock.UtcNow);
        AuditAssert.SnapshotKeys(auditEvent, "password_reset_ticket_id", "issued_by_user_id", "channel", "issued_at", "expires_at", "consumed_at");
        var snapshot = AuditAssert.SnapshotOf(auditEvent);
        Assert.Equal(ticket.Id.ToString(), snapshot["password_reset_ticket_id"]);
        Assert.Null(snapshot["issued_by_user_id"]);
        Assert.Equal("operator", snapshot["channel"]);
        AuditAssert.SnapshotTime(snapshot, "issued_at", ticket.IssuedAt);
        AuditAssert.SnapshotTime(snapshot, "expires_at", ticket.ExpiresAt);
        AuditAssert.SnapshotTime(snapshot, "consumed_at", _clock.UtcNow);
        AuditAssert.ContainsNone(auditEvent, code, "NewPassw0rd", user.PasswordHash, Convert.ToHexString(ticket.CodeHash));
    }

    [Fact]
    public async Task Complete_Failure_StagesNoAuditEventAndOpensNoTransaction()
    {
        var (user, ticket, _) = SeedUserWithOpenTicket();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            user.LoginIdentifier.Value, "WRONG-CODE-0000-0000", "NewPassw0rd", "corr-bad", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Empty(_auditWriter.Staged);
        Assert.False(_unitOfWork.TransactionOpen);
        Assert.False(_unitOfWork.Committed);
        Assert.False(ticket.ConsumedAt.HasValue);
    }
}
