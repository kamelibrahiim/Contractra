using Contractra.Application.Audit;
using Contractra.Application.Common;
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

    public ChangePasswordUseCase(
        IUserRepository users,
        ISessionStore sessions,
        IPasswordHasher hasher,
        IAuditWriter auditWriter,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _sessions = sessions;
        _hasher = hasher;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<UseCaseResult> ExecuteAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (request.NewPassword.Length < MinPasswordLength)
        {
            return UseCaseResult.Failure("auth.password_too_short", "The new password must be at least 8 characters.");
        }

        if (request.NewPassword.Length > MaxPasswordLength)
        {
            return UseCaseResult.Failure("auth.password_too_long", "The new password must be at most 128 characters.");
        }

        var user = await _users.FindByIdAsync(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException("ChangePassword requires an authenticated caller; the user must exist.");

        if (!user.HasUsablePassword
            || _hasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return UseCaseResult.Failure("auth.current_password_incorrect", "The current password is incorrect.");
        }

        if (request.NewPassword == request.CurrentPassword)
        {
            return UseCaseResult.Failure("auth.password_same_as_current", "The new password must differ from the current password.");
        }

        var now = _clock.UtcNow;
        var newHash = _hasher.Hash(request.NewPassword);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        user.ChangePassword(newHash);
        await _sessions.RevokeAllActiveAsync(
            user.Id, SessionRevokeReason.PasswordChanged, now, exceptSessionId: request.SessionId, cancellationToken);

        _auditWriter.Stage(AuditEvent.ForUser(
            AuditActionType.PasswordChanged,
            actorUserId: user.Id,
            // actor_role_code: contract §14.1 drafts a deterministic rule (the
            // actor's first active role by ordinal code order when the action
            // needs no permission, which is this case), but §19 OC-6 lists
            // that rule as still needing owner confirmation. Left null rather
            // than implementing an unconfirmed rule.
            actorRoleCode: null,
            entityType: "User",
            entityId: user.Id,
            occurredAtUtc: now,
            correlationId: request.CorrelationId,
            source: request.Source));

        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);
        return UseCaseResult.Success();
    }
}
