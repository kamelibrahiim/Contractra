using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Application.Identity.Auth;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Identity.UseCases;

/// <summary>
/// Auth contract v1.5 §5.5, owner decisions R1, R14, R16, R26; engineering
/// decision E13. Rotation of the current session's refresh token and the new
/// token response are Slice 6's job (the HTTP boundary); this use case does
/// only the password change, the "every other session" revocation and the
/// audit event, in one transaction.
///
/// Transaction boundary Slice 6 must respect: on success, this method has
/// already committed the password change, the revocation of every session
/// except <see cref="ChangePasswordRequest.SessionId"/>, and the
/// PasswordChanged audit event. That session itself is deliberately left
/// active (excluded from the revocation) specifically so the caller can
/// still rotate its refresh token afterward. If Slice 6 performs that
/// rotation as a separate step/transaction after calling this method, a
/// failure of the rotation step (or a crash between the two) leaves the
/// password changed and every other session revoked, but the current
/// session not rotated - the client has no new tokens to store even though
/// its old refresh token is still valid, so it is not locked out, but it
/// does not get the "same shape as refresh" response the contract promises
/// for this call. Slice 6 must either (a) perform the rotation inside the
/// same database transaction as this method (meaning this method's
/// transaction needs to stay open across both steps, which this class does
/// not do today), or (b) treat a post-commit rotation failure as a retryable
/// condition the client can recover via its normal refresh-on-401 flow, and
/// explicitly decide which before this feature is considered complete.
/// </summary>
public sealed class ChangePasswordUseCase
{
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 128;

    private readonly IUserRepository _users;
    private readonly ISessionStore _sessions;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserAccessReader _accessReader;

    public ChangePasswordUseCase(
        IUserRepository users,
        ISessionStore sessions,
        IPasswordHasher hasher,
        IAuditWriter auditWriter,
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserAccessReader accessReader)
    {
        _users = users;
        _sessions = sessions;
        _hasher = hasher;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _accessReader = accessReader;
    }

    public async Task<UseCaseResult> ExecuteAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        // Contract §5.5 lists currentPassword correctness first, then the
        // newPassword length rules, then the differs-from-current rule - this
        // method follows that order. Authentication and currentPassword
        // verification happen before either length bound is considered.
        var now = _clock.UtcNow;
        var user = await _users.FindByIdAsync(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException("ChangePassword requires an authenticated caller; the user must exist.");

        if (!user.HasUsablePassword
            || _hasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return UseCaseResult.Failure("auth.current_password_incorrect", "The current password is incorrect.");
        }

        // During a forced change the current password is the temporary one, which
        // must not have expired (contract §5.5, §10). Checked after the hash
        // verification above (which always runs, matching or not) so a correct hash
        // on an expired temporary password takes the same path and the same generic
        // code as a wrong hash, with no extra timing signal.
        if (user.MustChangePassword && user.TemporaryPasswordExpiresAt is { } expiresAt && expiresAt <= now)
        {
            return UseCaseResult.Failure("auth.current_password_incorrect", "The current password is incorrect.");
        }

        if (request.NewPassword.Length < MinPasswordLength)
        {
            return UseCaseResult.Failure("auth.password_too_short", "The new password must be at least 8 characters.");
        }

        if (request.NewPassword.Length > MaxPasswordLength)
        {
            return UseCaseResult.Failure("auth.password_too_long", "The new password must be at most 128 characters.");
        }

        if (request.NewPassword == request.CurrentPassword)
        {
            return UseCaseResult.Failure("auth.password_same_as_current", "The new password must differ from the current password.");
        }

        var newHash = _hasher.Hash(request.NewPassword);

        // actor_role_code (contract §14.1, owner decision OC-6, confirmed): this
        // action needs no permission (the caller changes their own password), so
        // the rule resolves to the actor's first active role by ordinal code
        // order - same rule and reader ScopedAccessDeniedAuditor uses for AccessDenied.
        var access = await _accessReader.GetAsync(user.Id, now, cancellationToken);
        var actorRoleCode = ActorRoleCodeRule.Resolve(access, requiredPermission: null);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        user.ChangePassword(newHash);
        await _sessions.RevokeAllActiveAsync(
            user.Id, SessionRevokeReason.PasswordChanged, now, exceptSessionId: request.SessionId, cancellationToken);

        _auditWriter.Stage(AuditEvent.ForUser(
            AuditActionType.PasswordChanged,
            actorUserId: user.Id,
            actorRoleCode: actorRoleCode,
            entityType: "User",
            entityId: user.Id,
            occurredAtUtc: now,
            correlationId: request.CorrelationId,
            source: request.Source));

        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);
        return UseCaseResult.Success();
    }
}
