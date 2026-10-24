using Contractra.Application.Identity.UseCases;
using Contractra.Application.Tests.Identity.Auth;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity;

/// <summary>Auth contract v1.5 §9.5, §11, owner decisions R4, R25.</summary>
public class ReactivateUserUseCaseTests
{
    private static readonly Guid ActorId = Guid.CreateVersion7();

    private readonly FakeUserRepository _users = new();
    private readonly FakeAuditWriter _auditWriter = new();
    private readonly FakeClock _clock = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeUserAccessReader _accessReader = new();

    private ReactivateUserUseCase CreateUseCase() => new(_users, _auditWriter, _clock, _unitOfWork, _accessReader);

    [Fact]
    public async Task Reactivate_ADisabledUser_SetsStatusAndAudits()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Disabled, "hash", mustChangePassword: false);
        _users.Seed(user);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ReactivateUserRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(_unitOfWork.Committed);

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal(AuditActionType.UserReactivated, auditEvent.ActionType);
        Assert.Equal("Disabled", auditEvent.PreviousStatus);
        Assert.Equal("Active", auditEvent.NewStatus);
    }

    [Fact]
    public async Task Reactivate_AnUnknownUser_Fails()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ReactivateUserRequest(Guid.CreateVersion7(), ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.not_found", result.Error!.Code);
    }

    [Fact]
    public async Task Reactivate_AnAlreadyActiveUser_Fails()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Active, "hash", mustChangePassword: false);
        _users.Seed(user);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ReactivateUserRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.already_active", result.Error!.Code);
        Assert.Empty(_auditWriter.Staged);
    }

    [Fact]
    public async Task Reactivate_TheBootstrapAdmin_IsProtected()
    {
        var adminRole = new Role(new RoleCode("Admin"), RoleStatus.Active);
        var payrollView = new Permission(new PermissionCode(PermissionCode.PayrollView), PermissionStatus.Active);
        var admin = User.CreateBootstrapAdmin(
            new LoginIdentifier("bootstrap.admin"), "hash", adminRole, payrollView, _clock.UtcNow, _clock.UtcNow);
        _users.Seed(admin);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new ReactivateUserRequest(admin.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.bootstrap_account_protected", result.Error!.Code);
        Assert.Empty(_auditWriter.Staged);
    }

    [Fact]
    public async Task Reactivate_AuditEvent_CarriesActorTargetSourceCorrelationAndTheStatusTransition()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Disabled, "hash", mustChangePassword: false);
        _users.Seed(user);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new ReactivateUserRequest(user.Id, ActorId, "corr-react", AuditSource.Web));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        // OC-6 resolved: ActorId has no active role in this fake, so the
        // confirmed rule itself (not an unresolved OC-6) is what yields null here.
        AuditAssert.Event(
            auditEvent, AuditActionType.UserReactivated, ActorId, user.Id, "corr-react", AuditSource.Web, _clock.UtcNow,
            previousStatus: "Disabled", newStatus: "Active",
            actorRoleCodeStillPendingOc6: false, expectedActorRoleCode: null);
        Assert.Null(auditEvent.Snapshot);
    }

    // Contract §14.1, owner decision OC-6: reactivating a user requires
    // Users.Manage, so the rule resolves to the active role through which the
    // actor held it (several → first by ordinal code order).
    [Fact]
    public async Task Reactivate_AuditEvent_ActorRoleCode_IsTheRoleGrantingUsersManage()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Disabled, "hash", mustChangePassword: false);
        _users.Seed(user);
        _accessReader.Set(
            ActorId, ["Admin", "Accountant"],
            new() { ["Admin"] = ["Users.Manage"], ["Accountant"] = ["Users.Manage"] });
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new ReactivateUserRequest(user.Id, ActorId, "corr-react", AuditSource.Web));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal("Accountant", auditEvent.ActorRoleCode);
    }

    // No role grants Users.Manage directly: the rule falls back to the
    // actor's first active role.
    [Fact]
    public async Task Reactivate_AuditEvent_ActorRoleCode_FallsBackToTheFirstActiveRole_WhenNoRoleGrantsUsersManage()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Disabled, "hash", mustChangePassword: false);
        _users.Seed(user);
        _accessReader.Set(ActorId, ["OfficeStaff", "Engineer"], direct: ["Users.Manage"]);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new ReactivateUserRequest(user.Id, ActorId, "corr-react", AuditSource.Web));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal("Engineer", auditEvent.ActorRoleCode);
    }
}
