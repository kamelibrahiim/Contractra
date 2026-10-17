using Contractra.Application.Identity.UseCases;
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

    private ReactivateUserUseCase CreateUseCase() => new(_users, _auditWriter, _clock, _unitOfWork);

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
        AuditAssert.Event(
            auditEvent, AuditActionType.UserReactivated, ActorId, user.Id, "corr-react", AuditSource.Web, _clock.UtcNow,
            previousStatus: "Disabled", newStatus: "Active");
        Assert.Null(auditEvent.Snapshot);
    }
}
