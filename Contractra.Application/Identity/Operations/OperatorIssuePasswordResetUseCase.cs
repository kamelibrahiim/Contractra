using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Application.Identity.Operations;

/// <param name="LoginIdentifier">The target account (case-insensitive, as login).</param>
/// <param name="Reason">The operator's <c>--reason</c>: 1-1000 characters, no leading or trailing whitespace. Must not contain secrets.</param>
/// <param name="CorrelationId">One per command run (contract §13).</param>
public sealed record OperatorIssuePasswordResetRequest(string LoginIdentifier, string Reason, string CorrelationId);

/// <param name="Code">The reset code. For the operator's terminal only: never logged, audited or stored (only its hash is).</param>
public sealed record OperatorIssuePasswordResetResult(Guid UserId, Guid TicketId, string Code, DateTimeOffset ExpiresAt);

/// <summary>
/// Operator command <c>issue-password-reset --login &lt;identifier&gt; --reason &lt;text&gt;</c>
/// (auth contract v1.5 §10A "Interim operator procedure", §18; owner decisions
/// R15, R44; repository ADR 0006 §5.3, E34). Same preconditions and effects as
/// the endpoint of §9.6 - target Active, not the bootstrap admin, has chosen a
/// password; the current password is invalidated, every session revoked, every
/// earlier open ticket superseded - with channel <c>operator</c> and no issuing
/// user. Audited as <c>PasswordResetIssued</c> with a System actor, source
/// SystemJob, the operator's reason and idempotency key
/// <c>PasswordResetIssued:command:&lt;run correlation id&gt;</c>, in the same
/// transaction. Repeating the command deliberately issues a new ticket that
/// supersedes the previous one, as the endpoint does.
/// </summary>
public sealed class OperatorIssuePasswordResetUseCase
{
    public const string Channel = "operator";
    public const int MaxReasonLength = 1000;

    private readonly IUserRepository _users;
    private readonly ISessionStore _sessions;
    private readonly IPasswordResetTicketStore _tickets;
    private readonly IResetCodeGenerator _codes;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeSpan _codeLifetime;
    private readonly ILogger<OperatorIssuePasswordResetUseCase> _logger;

    public OperatorIssuePasswordResetUseCase(
        IUserRepository users,
        ISessionStore sessions,
        IPasswordResetTicketStore tickets,
        IResetCodeGenerator codes,
        IAuditWriter auditWriter,
        IClock clock,
        IUnitOfWork unitOfWork,
        TimeSpan? codeLifetime = null,
        ILogger<OperatorIssuePasswordResetUseCase>? logger = null)
    {
        _users = users;
        _sessions = sessions;
        _tickets = tickets;
        _codes = codes;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _codeLifetime = codeLifetime ?? TimeSpan.FromHours(24);
        _logger = logger ?? NullLogger<OperatorIssuePasswordResetUseCase>.Instance;
    }

    public async Task<UseCaseResult<OperatorIssuePasswordResetResult>> ExecuteAsync(
        OperatorIssuePasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        if (!OperatorReason.IsValid(request.Reason))
        {
            return Fail("validation.failed", $"--reason is required: 1 to {MaxReasonLength} characters with no leading or trailing whitespace.");
        }

        var user = await _users.FindByLoginIdentifierAsync(request.LoginIdentifier, cancellationToken);
        if (user is null)
        {
            return Fail("users.not_found", "No user has this login identifier.");
        }

        if (user.IsBootstrapAdmin)
        {
            _logger.LogWarning(
                "Operator password reset refused: the target is the protected bootstrap admin. CorrelationId={CorrelationId} UserId={UserId}",
                request.CorrelationId, user.Id);
            return Fail("users.bootstrap_account_protected", "The bootstrap admin is protected; use reset-bootstrap-password.");
        }

        if (user.Status != UserStatus.Active)
        {
            return Fail("users.already_disabled", "Reactivate the user before issuing a reset.");
        }

        if (user.MustChangePassword)
        {
            return Fail("users.initial_password_pending", "This account is still on its initial temporary password; re-issue it from the Admin app instead.");
        }

        var now = _clock.UtcNow;
        var generated = _codes.Generate();
        var expiresAt = now.Add(_codeLifetime);
        var ticket = PasswordResetTicket.Issue(user.Id, generated.Hash, now, expiresAt, issuedByUserId: null, Channel);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        user.BeginPasswordReset();
        await _tickets.IssueAsync(ticket, cancellationToken);
        await _sessions.RevokeAllActiveAsync(user.Id, SessionRevokeReason.PasswordResetIssued, now, cancellationToken: cancellationToken);

        _auditWriter.Stage(AuditEvent.ForSystem(
            AuditActionType.PasswordResetIssued,
            entityType: "User",
            entityId: user.Id,
            occurredAtUtc: now,
            correlationId: request.CorrelationId,
            idempotencyOperationKey: $"command:{request.CorrelationId}",
            reason: request.Reason,
            snapshot: new Dictionary<string, string?>
            {
                ["password_reset_ticket_id"] = ticket.Id.ToString(),
                ["issued_by_user_id"] = null,
                ["channel"] = Channel,
                ["issued_at"] = now.ToString("O"),
                ["expires_at"] = expiresAt.ToString("O"),
                ["consumed_at"] = null,
            }));

        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);

        _logger.LogInformation(
            "Operator password reset issued. CorrelationId={CorrelationId} UserId={UserId} TicketId={TicketId}",
            request.CorrelationId, user.Id, ticket.Id);

        return UseCaseResult<OperatorIssuePasswordResetResult>.Success(
            new OperatorIssuePasswordResetResult(user.Id, ticket.Id, generated.Code, expiresAt));
    }

    private static UseCaseResult<OperatorIssuePasswordResetResult> Fail(string code, string message) =>
        UseCaseResult<OperatorIssuePasswordResetResult>.Failure(code, message);
}

/// <summary>The reason rule of repository ADR 0006 E34, checked before anything is read or written.</summary>
internal static class OperatorReason
{
    public static bool IsValid(string? reason) =>
        !string.IsNullOrEmpty(reason)
        && reason.Length <= OperatorIssuePasswordResetUseCase.MaxReasonLength
        && reason == reason.Trim();
}
