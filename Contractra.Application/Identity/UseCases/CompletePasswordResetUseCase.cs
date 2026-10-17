using System.Security.Cryptography;
using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Identity.UseCases;

/// <summary>
/// Auth contract v1.5 §5.6, owner decision R44. No session is created; the
/// client sends the user to the login screen. Every failure path - unknown
/// identifier, disabled account, no open ticket, wrong code, expired code -
/// returns the same generic error and does the same hashing work, so timing
/// cannot distinguish them (§5.6).
/// </summary>
public sealed class CompletePasswordResetUseCase
{
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 128;
    private const string GenericErrorCode = "auth.reset_code_invalid";
    private const string GenericErrorMessage = "The reset code is invalid, expired, or already used.";

    private readonly IUserRepository _users;
    private readonly ISessionStore _sessions;
    private readonly IPasswordResetTicketStore _tickets;
    private readonly IResetCodeGenerator _codes;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;

    public CompletePasswordResetUseCase(
        IUserRepository users,
        ISessionStore sessions,
        IPasswordResetTicketStore tickets,
        IResetCodeGenerator codes,
        IPasswordHasher hasher,
        IAuditWriter auditWriter,
        IClock clock,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _sessions = sessions;
        _tickets = tickets;
        _codes = codes;
        _hasher = hasher;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<UseCaseResult> ExecuteAsync(CompletePasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        if (request.NewPassword.Length < MinPasswordLength)
        {
            return UseCaseResult.Failure("auth.password_too_short", "The new password must be at least 8 characters.");
        }

        if (request.NewPassword.Length > MaxPasswordLength)
        {
            return UseCaseResult.Failure("auth.password_too_long", "The new password must be at most 128 characters.");
        }

        var submittedHash = _codes.Hash(request.ResetCode);
        var user = await _users.FindByLoginIdentifierAsync(request.LoginIdentifier, cancellationToken);
        var now = _clock.UtcNow;

        var ticket = user is { Status: UserStatus.Active }
            ? await _tickets.FindOpenAsync(user.Id, cancellationToken)
            : null;

        // Always hash-compare, even with no ticket, so a missing ticket and a
        // wrong code take the same time.
        var ticketHash = ticket?.CodeHash ?? submittedHash;
        var codeMatches = CryptographicOperations.FixedTimeEquals(ticketHash, submittedHash);

        if (user is not { Status: UserStatus.Active } || ticket is null || !codeMatches || !ticket.IsValidAt(now))
        {
            return UseCaseResult.Failure(GenericErrorCode, GenericErrorMessage);
        }

        var newHash = _hasher.Hash(request.NewPassword);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        user.ChangePassword(newHash);
        ticket.Consume(now);
        await _sessions.RevokeAllActiveAsync(user.Id, SessionRevokeReason.PasswordResetCompleted, now, cancellationToken: cancellationToken);

        _auditWriter.Stage(AuditEvent.ForUser(
            AuditActionType.PasswordResetCompleted,
            actorUserId: user.Id,
            // actor_role_code: as in ChangePasswordUseCase - contract §14.1's
            // rule for a no-permission self-action is drafted but unconfirmed
            // (§19 OC-6). Also: this endpoint is unauthenticated (§5.6), so no
            // session or token carries claims to read a role from even if the
            // rule were confirmed; it would have to be loaded from the user's
            // current UserRoles, same as every other case here.
            actorRoleCode: null,
            entityType: "User",
            entityId: user.Id,
            occurredAtUtc: now,
            correlationId: request.CorrelationId,
            source: request.Source,
            snapshot: new Dictionary<string, string?>
            {
                ["password_reset_ticket_id"] = ticket.Id.ToString(),
                ["issued_by_user_id"] = ticket.IssuedByUserId?.ToString(),
                ["channel"] = ticket.Channel,
                ["issued_at"] = ticket.IssuedAt.ToString("O"),
                ["expires_at"] = ticket.ExpiresAt.ToString("O"),
                ["consumed_at"] = now.ToString("O"),
            }));

        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);
        return UseCaseResult.Success();
    }
}
