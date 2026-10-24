using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Application.Identity.Auth;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Identity.UseCases;

/// <summary>
/// Auth contract v1.5 §9.5, §11, owner decisions R4, R25, R27. No session is
/// restored: sessions revoked by the earlier disable stay revoked (R25).
/// </summary>
public sealed class ReactivateUserUseCase
{
    private readonly IUserRepository _users;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserAccessReader _accessReader;

    public ReactivateUserUseCase(
        IUserRepository users, IAuditWriter auditWriter, IClock clock, IUnitOfWork unitOfWork, IUserAccessReader accessReader)
    {
        _users = users;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _accessReader = accessReader;
    }

    public async Task<UseCaseResult> ExecuteAsync(ReactivateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(request.TargetUserId, cancellationToken);
        if (user is null)
        {
            return UseCaseResult.Failure("users.not_found", "The user does not exist.");
        }

        if (user.IsBootstrapAdmin)
        {
            return UseCaseResult.Failure("users.bootstrap_account_protected", "The bootstrap admin is protected from this action.");
        }

        if (user.Status != UserStatus.Disabled)
        {
            return UseCaseResult.Failure("users.already_active", "The user is already active.");
        }

        var now = _clock.UtcNow;

        // actor_role_code (contract §14.1, owner decision OC-6, confirmed): this
        // action requires Users.Manage, so the rule resolves to the active role
        // through which the actor held it (several → first by ordinal code order).
        var access = await _accessReader.GetAsync(request.ActorUserId, now, cancellationToken);
        var actorRoleCode = ActorRoleCodeRule.Resolve(access, "Users.Manage");

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        user.Reactivate();

        _auditWriter.Stage(AuditEvent.ForUser(
            AuditActionType.UserReactivated,
            actorUserId: request.ActorUserId,
            actorRoleCode: actorRoleCode,
            entityType: "User",
            entityId: user.Id,
            occurredAtUtc: now,
            correlationId: request.CorrelationId,
            source: request.Source,
            previousStatus: "Disabled",
            newStatus: "Active"));

        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);
        return UseCaseResult.Success();
    }
}
