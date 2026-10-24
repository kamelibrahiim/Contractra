using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Application.Identity.Auth;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Identity.UseCases;

/// <summary>
/// Auth contract v1.5 §9.8, owner decisions R27; engineering decisions E3,
/// E4. For an account that has never left the forced-change state: its
/// temporary password expired, was lost before first use, or the account
/// predates credentials.
/// </summary>
public sealed class ReissueInitialPasswordUseCase
{
    private readonly IUserRepository _users;
    private readonly ISessionStore _sessions;
    private readonly IPasswordHasher _hasher;
    private readonly ITemporaryPasswordGenerator _temporaryPasswords;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeSpan _temporaryPasswordLifetime;
    private readonly IUserAccessReader _accessReader;

    public ReissueInitialPasswordUseCase(
        IUserRepository users,
        ISessionStore sessions,
        IPasswordHasher hasher,
        ITemporaryPasswordGenerator temporaryPasswords,
        IAuditWriter auditWriter,
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserAccessReader accessReader,
        TimeSpan? temporaryPasswordLifetime = null)
    {
        _users = users;
        _sessions = sessions;
        _hasher = hasher;
        _temporaryPasswords = temporaryPasswords;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _accessReader = accessReader;
        _temporaryPasswordLifetime = temporaryPasswordLifetime ?? TimeSpan.FromHours(72);
    }

    public async Task<UseCaseResult<ReissueInitialPasswordResult>> ExecuteAsync(
        ReissueInitialPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(request.TargetUserId, cancellationToken);
        if (user is null)
        {
            return Fail("users.not_found", "The user does not exist.");
        }

        if (user.IsBootstrapAdmin)
        {
            return Fail("users.bootstrap_account_protected", "The bootstrap admin is protected from this action.");
        }

        if (user.Status != UserStatus.Active)
        {
            return Fail("users.already_disabled", "Reactivate the user before re-issuing an initial password.");
        }

        if (!user.MustChangePassword)
        {
            return Fail("users.initial_password_not_reissuable", "This user has already chosen a password; issue a reset instead.");
        }

        var now = _clock.UtcNow;
        var temporaryPassword = _temporaryPasswords.Generate();
        var temporaryPasswordHash = _hasher.Hash(temporaryPassword);
        var expiresAt = now.Add(_temporaryPasswordLifetime);

        // actor_role_code (contract §14.1, owner decision OC-6, confirmed): this
        // action requires Users.Manage, so the rule resolves to the active role
        // through which the actor held it (several → first by ordinal code order).
        var access = await _accessReader.GetAsync(request.ActorUserId, now, cancellationToken);
        var actorRoleCode = ActorRoleCodeRule.Resolve(access, "Users.Manage");

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        user.ReissueTemporaryPassword(temporaryPasswordHash, expiresAt, now);
        await _sessions.RevokeAllActiveAsync(user.Id, SessionRevokeReason.InitialPasswordReissued, now, cancellationToken: cancellationToken);

        _auditWriter.Stage(AuditEvent.ForUser(
            AuditActionType.InitialPasswordReissued,
            actorUserId: request.ActorUserId,
            actorRoleCode: actorRoleCode,
            entityType: "User",
            entityId: user.Id,
            occurredAtUtc: now,
            correlationId: request.CorrelationId,
            source: request.Source));

        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);

        return UseCaseResult<ReissueInitialPasswordResult>.Success(
            new ReissueInitialPasswordResult(temporaryPassword, expiresAt));
    }

    private static UseCaseResult<ReissueInitialPasswordResult> Fail(string code, string message) =>
        UseCaseResult<ReissueInitialPasswordResult>.Failure(code, message);
}
