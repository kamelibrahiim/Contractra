using Contractra.Application.Audit;
using Contractra.Application.Common;
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

    public ReactivateUserUseCase(IUserRepository users, IAuditWriter auditWriter, IClock clock, IUnitOfWork unitOfWork)
    {
        _users = users;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
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

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        user.Reactivate();

        // actor_role_code: contract §14.1 would resolve this to the role
        // through which the actor held Users.Manage, but §19 OC-6 lists that
        // rule as still needing owner confirmation, and no permission-to-role
        // resolution exists anywhere in the codebase yet.
        _auditWriter.Stage(AuditEvent.ForUser(
            AuditActionType.UserReactivated,
            actorUserId: request.ActorUserId,
            actorRoleCode: null,
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
