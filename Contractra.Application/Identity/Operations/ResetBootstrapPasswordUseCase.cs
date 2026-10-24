using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Application.Identity.Operations;

/// <param name="Reason">The operator's <c>--reason</c>: 1-1000 characters, no leading or trailing whitespace. Must not contain secrets.</param>
/// <param name="CorrelationId">One per command run (contract §13).</param>
public sealed record ResetBootstrapPasswordRequest(string Reason, string CorrelationId);

/// <param name="NewPassword">For the operator's terminal only: never logged or audited (only its hash is stored).</param>
public sealed record ResetBootstrapPasswordResult(Guid UserId, string NewPassword);

/// <summary>
/// Operator command <c>reset-bootstrap-password --reason &lt;text&gt;</c> (auth
/// contract v1.5 §11 "Lost bootstrap password", owner decisions R29, R32).
/// Replaces the password of the single bootstrap admin with a freshly
/// generated one (contract §10's generator), keeps it exempt from the forced
/// change (R19), bumps the security version, revokes every session and audits
/// <c>BootstrapPasswordReset</c> (System actor, source SystemJob, the operator's
/// reason, key <c>BootstrapPasswordReset:command:&lt;run correlation id&gt;</c>) in
/// the same transaction. Repeating it generates another password.
///
/// OPEN DESIGN POINT: §11 delegates the command's exact invocation to Slice 5.
/// How the new password is chosen is not specified; this generates one and
/// shows it once on the operator's terminal, like the reset code of §10A. The
/// session revoke reason reuses <see cref="SessionRevokeReason.PasswordChanged"/>
/// because the nine reasons are fixed by a database CHECK.
/// </summary>
public sealed class ResetBootstrapPasswordUseCase
{
    private readonly IUserRepository _users;
    private readonly ISessionStore _sessions;
    private readonly IPasswordHasher _hasher;
    private readonly ITemporaryPasswordGenerator _passwords;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ResetBootstrapPasswordUseCase> _logger;

    public ResetBootstrapPasswordUseCase(
        IUserRepository users,
        ISessionStore sessions,
        IPasswordHasher hasher,
        ITemporaryPasswordGenerator passwords,
        IAuditWriter auditWriter,
        IClock clock,
        IUnitOfWork unitOfWork,
        ILogger<ResetBootstrapPasswordUseCase>? logger = null)
    {
        _users = users;
        _sessions = sessions;
        _hasher = hasher;
        _passwords = passwords;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger ?? NullLogger<ResetBootstrapPasswordUseCase>.Instance;
    }

    public async Task<UseCaseResult<ResetBootstrapPasswordResult>> ExecuteAsync(
        ResetBootstrapPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (!OperatorReason.IsValid(request.Reason))
        {
            return Fail("validation.failed", $"--reason is required: 1 to {OperatorIssuePasswordResetUseCase.MaxReasonLength} characters with no leading or trailing whitespace.");
        }

        var user = await _users.FindBootstrapAdminAsync(cancellationToken);
        if (user is null)
        {
            return Fail("users.not_found", "No bootstrap admin exists.");
        }

        var now = _clock.UtcNow;
        var newPassword = _passwords.Generate();
        var newHash = _hasher.Hash(newPassword);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // The aggregate allows the bootstrap admin's own password change (R26)
        // and keeps must_change_password false (R19).
        user.ChangePassword(newHash);
        await _sessions.RevokeAllActiveAsync(user.Id, SessionRevokeReason.PasswordChanged, now, cancellationToken: cancellationToken);

        _auditWriter.Stage(AuditEvent.ForSystem(
            AuditActionType.BootstrapPasswordReset,
            entityType: "User",
            entityId: user.Id,
            occurredAtUtc: now,
            correlationId: request.CorrelationId,
            idempotencyOperationKey: $"command:{request.CorrelationId}",
            reason: request.Reason));

        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);

        _logger.LogInformation(
            "Bootstrap admin password reset by operator command. CorrelationId={CorrelationId} UserId={UserId}",
            request.CorrelationId, user.Id);

        return UseCaseResult<ResetBootstrapPasswordResult>.Success(new ResetBootstrapPasswordResult(user.Id, newPassword));
    }

    private static UseCaseResult<ResetBootstrapPasswordResult> Fail(string code, string message) =>
        UseCaseResult<ResetBootstrapPasswordResult>.Failure(code, message);
}
