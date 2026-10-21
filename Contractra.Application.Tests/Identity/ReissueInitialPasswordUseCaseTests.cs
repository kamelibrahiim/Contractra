using Contractra.Application.Identity.UseCases;
using Contractra.Application.Tests.Identity.Auth;
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
    private readonly FakeUserAccessReader _accessReader = new();

    private ReissueInitialPasswordUseCase CreateUseCase() =>
        new(_users, _sessions, _hasher, _temporaryPasswords, _auditWriter, _clock, _unitOfWork, _accessReader);

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
        // OC-6 resolved: ActorId has no active role in this fake, so the
        // confirmed rule itself (not an unresolved OC-6) is what yields null here.
        AuditAssert.Event(
            auditEvent, AuditActionType.InitialPasswordReissued, ActorId, user.Id, "corr-reissue", AuditSource.Web, _clock.UtcNow,
            actorRoleCodeStillPendingOc6: false, expectedActorRoleCode: null);
        Assert.Null(auditEvent.Snapshot);
        AuditAssert.ContainsNone(auditEvent, result.Value!.TemporaryPassword, user.PasswordHash);
    }

    // Contract §14.1, owner decision OC-6: re-issuing an initial password
    // requires Users.Manage, so the rule resolves to the active role through
    // which the actor held it (several → first by ordinal code order).
    [Fact]
    public async Task Reissue_AuditEvent_ActorRoleCode_IsTheRoleGrantingUsersManage()
    {
        var user = SeedUserOnFirstTemporaryPassword();
        _accessReader.Set(
            ActorId, ["Admin", "Accountant"],
            new() { ["Admin"] = ["Users.Manage"], ["Accountant"] = ["Users.Manage"] });
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new ReissueInitialPasswordRequest(user.Id, ActorId, "corr-reissue", AuditSource.Web));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal("Accountant", auditEvent.ActorRoleCode);
    }

    // No role grants Users.Manage directly: the rule falls back to the
    // actor's first active role.
    [Fact]
    public async Task Reissue_AuditEvent_ActorRoleCode_FallsBackToTheFirstActiveRole_WhenNoRoleGrantsUsersManage()
    {
        var user = SeedUserOnFirstTemporaryPassword();
        _accessReader.Set(ActorId, ["OfficeStaff", "Engineer"], direct: ["Users.Manage"]);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new ReissueInitialPasswordRequest(user.Id, ActorId, "corr-reissue", AuditSource.Web));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal("Engineer", auditEvent.ActorRoleCode);
    }
}
