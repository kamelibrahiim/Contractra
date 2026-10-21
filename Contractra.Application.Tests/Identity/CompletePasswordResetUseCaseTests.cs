using Contractra.Application.Identity.UseCases;
using Contractra.Application.Tests.Identity.Auth;
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
    private readonly FakeUserAccessReader _accessReader = new();

    private CompletePasswordResetUseCase CreateUseCase() =>
        new(_users, _sessions, _tickets, _codes, _hasher, _auditWriter, _clock, _unitOfWork, _accessReader);

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
        var (user, ticket, code) = SeedUserWithOpenTicket();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            user.LoginIdentifier.Value, code, tooShort, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.password_too_short", result.Error!.Code);
        Assert.True(ticket.IsOpen);
        Assert.False(_unitOfWork.TransactionOpen);
        Assert.False(_unitOfWork.Committed);
    }

    // Contract §5.6 orders the new-password rules last: the identifier, the
    // account status and the code are checked first, and every one of those
    // failures returns the same generic error regardless of what the new
    // password looks like.
    [Fact]
    public async Task Complete_WithAWrongCode_AndATooShortNewPassword_StillReturnsTheGenericResetCodeError()
    {
        var (user, ticket, _) = SeedUserWithOpenTicket();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            user.LoginIdentifier.Value, "WRNG-0000-0000-0000", "x", "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.reset_code_invalid", result.Error!.Code);
        Assert.True(ticket.IsOpen);
    }

    // Unlike the minimum-length rule, the §1 transport limit on newPassword
    // (at most 128 characters) is checked before any lookup (owner-approved
    // behavior): a too-long password is rejected on its own, even paired with
    // a wrong code, and never reaches the identifier/ticket/code check at all.
    // Proven by call counts, not by the absence of a matching seeded user: an
    // unknown login proves nothing about whether a lookup was attempted, only
    // about what it would have found.
    [Fact]
    public async Task Complete_WithAWrongCode_AndATooLongNewPassword_RejectsTheLengthFirst_WithoutAnyLookupOrHashing()
    {
        var (user, _, _) = SeedUserWithOpenTicket();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            user.LoginIdentifier.Value, "WRNG-0000-0000-0000", new string('a', 129), "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.password_too_long", result.Error!.Code);
        Assert.Equal(0, _users.FindByLoginIdentifierCallCount);
        Assert.Equal(0, _codes.HashCallCount);
        Assert.Equal(0, _hasher.HashCallCount);
    }

    // Same guard, exercised directly (bypassing any HTTP-layer check), with a
    // real seeded user and a real open ticket available to find - so a pass
    // here cannot be explained by "there was nothing to look up".
    [Fact]
    public async Task Complete_WithATooLongNewPassword_NeverLooksUpTheUserOrHashesAnything()
    {
        var (user, ticket, code) = SeedUserWithOpenTicket();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            user.LoginIdentifier.Value, code, new string('a', 129), "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.password_too_long", result.Error!.Code);
        Assert.True(ticket.IsOpen);
        Assert.Equal(0, _users.FindByLoginIdentifierCallCount);
        Assert.Equal(0, _codes.HashCallCount);
        Assert.Equal(0, _hasher.HashCallCount);
    }

    [Fact]
    public async Task Complete_WithAnUnknownIdentifier_AndATooShortNewPassword_StillReturnsTheGenericResetCodeError()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            "no.such.user", "CODE-0001-0000-0000", "x", "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.reset_code_invalid", result.Error!.Code);
    }

    [Fact]
    public async Task Complete_ForADisabledUser_AndATooShortNewPassword_StillReturnsTheGenericResetCodeError()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Disabled, "!", mustChangePassword: false);
        _users.Seed(user);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            "eng.ahmed", "CODE-0001-0000-0000", "x", "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.reset_code_invalid", result.Error!.Code);
    }

    [Fact]
    public async Task Complete_AuditEvent_NamesTheUserAsActorAndSnapshotsTheConsumedTicket()
    {
        var (user, ticket, code) = SeedUserWithOpenTicket();
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            user.LoginIdentifier.Value, code, "NewPassw0rd", "corr-complete", AuditSource.EmployeeApp));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        // OC-6 resolved: this seeded user has no active role, so the confirmed
        // rule itself (not an unresolved OC-6) is what yields null here.
        AuditAssert.Event(
            auditEvent, AuditActionType.PasswordResetCompleted, user.Id, user.Id, "corr-complete", AuditSource.EmployeeApp, _clock.UtcNow,
            actorRoleCodeStillPendingOc6: false, expectedActorRoleCode: null);
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

    // Contract §14.1, owner decision OC-6: completing one's own reset needs no
    // permission, so the rule resolves to the actor's first active role by
    // ordinal code order - "Accountant" sorts before "Engineer" and "OfficeStaff".
    [Fact]
    public async Task Complete_AuditEvent_ActorRoleCode_IsTheFirstActiveRoleByOrdinalOrder_WhenTheActorHasSeveral()
    {
        var (user, _, code) = SeedUserWithOpenTicket();
        _accessReader.Set(user.Id, ["OfficeStaff", "Accountant", "Engineer"]);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new CompletePasswordResetRequest(
            user.LoginIdentifier.Value, code, "NewPassw0rd", "corr-complete", AuditSource.EmployeeApp));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal("Accountant", auditEvent.ActorRoleCode);
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
