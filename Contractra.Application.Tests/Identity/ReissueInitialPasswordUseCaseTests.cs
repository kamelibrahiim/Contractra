using Contractra.Application.Identity.UseCases;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity;

/// <summary>Auth contract v1.5 §9.8, engineering decisions E3, E4.</summary>
public class ReissueInitialPasswordUseCaseTests
{
    private static readonly Guid ActorId = Guid.CreateVersion7();

    private readonly FakeUserRepository _users = new();
    private readonly FakeSessionStore _sessions = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeTemporaryPasswordGenerator _temporaryPasswords = new();
    private readonly FakeAuditWriter _auditWriter = new();
    private readonly FakeClock _clock = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private ReissueInitialPasswordUseCase CreateUseCase() =>
        new(_users, _sessions, _hasher, _temporaryPasswords, _auditWriter, _clock, _unitOfWork);

    private User SeedUserOnFirstTemporaryPassword(string login = "eng.ahmed") =>
        SeedUser(login, mustChangePassword: true, _clock.UtcNow.AddHours(1));

    private User SeedUser(string login, bool mustChangePassword, DateTimeOffset? expiresAt = null)
    {
        var user = new User(new LoginIdentifier(login), UserStatus.Active, "hash", mustChangePassword, expiresAt);
        _users.Seed(user);
        return user;
    }

    [Fact]
    public async Task Reissue_ForAUserStillOnItsFirstTemporaryPassword_IssuesANewOne()
    {
        var user = SeedUserOnFirstTemporaryPassword();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ReissueInitialPasswordRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value!.TemporaryPassword);
        Assert.Equal(_hasher.Hash(result.Value.TemporaryPassword), user.PasswordHash);
        Assert.True(user.MustChangePassword);
        Assert.True(_unitOfWork.Committed);
    }

    [Fact]
    public async Task Reissue_RevokesAllSessionsAndStagesInitialPasswordReissuedAudit()
    {
        var user = SeedUserOnFirstTemporaryPassword();
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new ReissueInitialPasswordRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        var call = Assert.Single(_sessions.RevokeAllCalls);
        Assert.Equal(user.Id, call.UserId);
        Assert.Equal(SessionRevokeReason.InitialPasswordReissued, call.Reason);

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal(AuditActionType.InitialPasswordReissued, auditEvent.ActionType);
    }

    [Fact]
    public async Task Reissue_AnUnknownUser_Fails()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ReissueInitialPasswordRequest(Guid.CreateVersion7(), ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.not_found", result.Error!.Code);
    }

    [Fact]
    public async Task Reissue_ADisabledUser_Fails()
    {
        var user = SeedUser("eng.ahmed", mustChangePassword: true, _clock.UtcNow.AddHours(1));
        user.Disable();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ReissueInitialPasswordRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.already_disabled", result.Error!.Code);
    }

    [Fact]
    public async Task Reissue_AUserWhoHasAlreadyChosenAPassword_Fails()
    {
        var user = SeedUser("eng.ahmed", mustChangePassword: false);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ReissueInitialPasswordRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.initial_password_not_reissuable", result.Error!.Code);
    }

    [Fact]
    public async Task Reissue_TheBootstrapAdmin_IsProtected()
    {
        var adminRole = new Role(new RoleCode("Admin"), RoleStatus.Active);
        var payrollView = new Permission(new PermissionCode(PermissionCode.PayrollView), PermissionStatus.Active);
        var admin = User.CreateBootstrapAdmin(
            new LoginIdentifier("bootstrap.admin"), "hash", adminRole, payrollView, _clock.UtcNow, _clock.UtcNow);
        _users.Seed(admin);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ReissueInitialPasswordRequest(admin.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.bootstrap_account_protected", result.Error!.Code);
    }

    [Fact]
    public async Task Reissue_AuditEvent_CarriesActorTargetSourceAndCorrelationAndNoSecret()
    {
        var user = SeedUserOnFirstTemporaryPassword();
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ReissueInitialPasswordRequest(user.Id, ActorId, "corr-reissue", AuditSource.Web));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        AuditAssert.Event(auditEvent, AuditActionType.InitialPasswordReissued, ActorId, user.Id, "corr-reissue", AuditSource.Web, _clock.UtcNow);
        Assert.Null(auditEvent.Snapshot);
        AuditAssert.ContainsNone(auditEvent, result.Value!.TemporaryPassword, user.PasswordHash);
    }
}
