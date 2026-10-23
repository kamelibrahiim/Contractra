using Contractra.Application.Identity.UseCases;
using Contractra.Application.Tests.Identity.Auth;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity;

/// <summary>Auth contract v1.5 §9.4, §11.</summary>
public class DisableUserUseCaseTests
{
    private static readonly Guid ActorId = Guid.CreateVersion7();

    private readonly FakeUserRepository _users = new();
    private readonly FakeSessionStore _sessions = new();
    private readonly FakeAuditWriter _auditWriter = new();
    private readonly FakeClock _clock = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeUserAccessReader _accessReader = new();
    private readonly FakeCredentialIssuanceStore _issuances = new();

    private DisableUserUseCase CreateUseCase() => new(_users, _sessions, _auditWriter, _clock, _unitOfWork, _accessReader, _issuances);

    // Contract v1.6 §23.4: disabling an account cancels its open credential issuance and purges the sealed copy.
    [Fact]
    public async Task Disable_CancelsTheOpenCredentialIssuanceAndPurgesItsSealedCopy()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Active, "hash", mustChangePassword: false);
        _users.Seed(user);
        var issuance = CredentialIssuance.Queue(
            Guid.CreateVersion7(), user.Id, CredentialIssuanceKind.Initial, ActorId, _clock.UtcNow,
            "+201000000001", "verbal", [1, 2], [3, 4], "k1", TimeSpan.FromHours(24));
        _issuances.Add(issuance);

        var result = await CreateUseCase().ExecuteAsync(new DisableUserRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.Equal(CredentialDeliveryStatus.Cancelled, issuance.Status);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task Disable_AnActiveUser_SetsStatusRevokesSessionsAndAudits()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Active, "hash", mustChangePassword: false);
        _users.Seed(user);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new DisableUserRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Disabled, user.Status);
        Assert.True(_unitOfWork.Committed);

        var call = Assert.Single(_sessions.RevokeAllCalls);
        Assert.Equal(user.Id, call.UserId);
        Assert.Equal(SessionRevokeReason.UserDisabled, call.Reason);
        Assert.Null(call.ExceptSessionId);

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal(AuditActionType.UserDisabled, auditEvent.ActionType);
        Assert.Equal("Active", auditEvent.PreviousStatus);
        Assert.Equal("Disabled", auditEvent.NewStatus);
    }

    [Fact]
    public async Task Disable_AnUnknownUser_Fails()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new DisableUserRequest(Guid.CreateVersion7(), ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.not_found", result.Error!.Code);
    }

    [Fact]
    public async Task Disable_AnAlreadyDisabledUser_Fails()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Disabled, "hash", mustChangePassword: false);
        _users.Seed(user);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new DisableUserRequest(user.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.already_disabled", result.Error!.Code);
        Assert.Empty(_auditWriter.Staged);
    }

    [Fact]
    public async Task Disable_TheBootstrapAdmin_IsProtected_AndChangesNothing()
    {
        var adminRole = new Role(new RoleCode("Admin"), RoleStatus.Active);
        var payrollView = new Permission(new PermissionCode(PermissionCode.PayrollView), PermissionStatus.Active);
        var admin = User.CreateBootstrapAdmin(
            new LoginIdentifier("bootstrap.admin"), "hash", adminRole, payrollView, _clock.UtcNow, _clock.UtcNow);
        _users.Seed(admin);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new DisableUserRequest(admin.Id, ActorId, "corr-1", AuditSource.Web));

        Assert.False(result.IsSuccess);
        Assert.Equal("users.bootstrap_account_protected", result.Error!.Code);
        Assert.Equal(UserStatus.Active, admin.Status);
        Assert.False(_unitOfWork.TransactionOpen);
        Assert.False(_unitOfWork.Committed);
        Assert.Empty(_auditWriter.Staged);
        Assert.Empty(_sessions.RevokeAllCalls);
    }

    [Fact]
    public async Task Disable_AuditEvent_CarriesActorTargetSourceCorrelationAndTheStatusTransition()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Active, "hash", mustChangePassword: false);
        _users.Seed(user);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new DisableUserRequest(user.Id, ActorId, "corr-disable", AuditSource.Web));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        // OC-6 resolved: ActorId has no active role in this fake, so the
        // confirmed rule itself (not an unresolved OC-6) is what yields null here.
        AuditAssert.Event(
            auditEvent, AuditActionType.UserDisabled, ActorId, user.Id, "corr-disable", AuditSource.Web, _clock.UtcNow,
            previousStatus: "Active", newStatus: "Disabled",
            actorRoleCodeStillPendingOc6: false, expectedActorRoleCode: null);
        Assert.Null(auditEvent.Snapshot);
    }

    // Contract §14.1, owner decision OC-6: disabling a user requires Users.Manage,
    // so the rule resolves to the active role through which the actor held it
    // (several → first by ordinal code order) - "Accountant" sorts before "Admin".
    [Fact]
    public async Task Disable_AuditEvent_ActorRoleCode_IsTheRoleGrantingUsersManage()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Active, "hash", mustChangePassword: false);
        _users.Seed(user);
        _accessReader.Set(
            ActorId, ["Admin", "Accountant"],
            new() { ["Admin"] = ["Users.Manage"], ["Accountant"] = ["Users.Manage"] });
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new DisableUserRequest(user.Id, ActorId, "corr-disable", AuditSource.Web));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal("Accountant", auditEvent.ActorRoleCode);
    }

    // No role grants Users.Manage directly: the rule falls back to the
    // actor's first active role.
    [Fact]
    public async Task Disable_AuditEvent_ActorRoleCode_FallsBackToTheFirstActiveRole_WhenNoRoleGrantsUsersManage()
    {
        var user = new User(new LoginIdentifier("eng.ahmed"), UserStatus.Active, "hash", mustChangePassword: false);
        _users.Seed(user);
        _accessReader.Set(ActorId, ["OfficeStaff", "Engineer"], direct: ["Users.Manage"]);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new DisableUserRequest(user.Id, ActorId, "corr-disable", AuditSource.Web));

        var auditEvent = Assert.Single(_auditWriter.Staged);
        Assert.Equal("Engineer", auditEvent.ActorRoleCode);
    }
}
