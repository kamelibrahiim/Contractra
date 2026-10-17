using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Identity.UseCases;

/// <summary>
/// Auth contract v1.5 §9.4, §11, owner decisions C5, R5, R27, R39. Bootstrap
/// protection is checked here (defence in depth; the aggregate refuses it
/// too) before the state-transition check, as the contract's §9 check order
/// requires.
/// </summary>
public sealed class DisableUserUseCase
{
    private readonly IUserRepository _users;
    private readonly ISessionStore _sessions;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public DisableUserUseCase(
        IUserRepository users, ISessionStore sessions, IAuditWriter auditWriter, IClock clock, IUnitOfWork unitOfWork)
    {
        _users = users;
        _sessions = sessions;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<UseCaseResult> ExecuteAsync(DisableUserRequest request, CancellationToken cancellationToken = default)
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

        if (user.Status != UserStatus.Active)
        {
            return UseCaseResult.Failure("users.already_disabled", "The user is already disabled.");
        }

        var now = _clock.UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        user.Disable();
        await _sessions.RevokeAllActiveAsync(user.Id, SessionRevokeReason.UserDisabled, now, cancellationToken: cancellationToken);

        // actor_role_code: contract §14.1 would resolve this to the role
        // through which the actor held Users.Manage, but §19 OC-6 lists that
        // rule as still needing owner confirmation, and no permission-to-role
        // resolution exists anywhere in the codebase yet.
        _auditWriter.Stage(AuditEvent.ForUser(
            AuditActionType.UserDisabled,
            actorUserId: request.ActorUserId,
            actorRoleCode: null,
            entityType: "User",
            entityId: user.Id,
            occurredAtUtc: now,
            correlationId: request.CorrelationId,
            source: request.Source,
            previousStatus: "Active",
            newStatus: "Disabled"));

        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);
        return UseCaseResult.Success();
    }
}
