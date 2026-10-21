using System.Security.Cryptography;
using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Application.Identity.Auth;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Identity.UseCases;

/// <summary>
/// Auth contract v1.5 §5.6, owner decision R44. No session is created; the
/// client sends the user to the login screen. Among the identity-related
/// failure paths - unknown identifier, disabled account, no open ticket,
/// wrong code, expired code - every one returns the same generic error and
/// does the same hashing work, so timing cannot distinguish them (§5.6). An
/// over-128-character new password is a §1 transport-limit violation, not an
/// identity signal: it is rejected before any lookup or hash, with its own
/// code, same as the real endpoint's earlier §1 check over
/// <see cref="Contractra.Application.Identity.Auth.ThrottledPasswordResetCompletion"/>.
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
    private readonly IUserAccessReader _accessReader;

    public CompletePasswordResetUseCase(
        IUserRepository users,
        ISessionStore sessions,
        IPasswordResetTicketStore tickets,
        IResetCodeGenerator codes,
        IPasswordHasher hasher,
        IAuditWriter auditWriter,
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserAccessReader accessReader)
    {
        _users = users;
        _sessions = sessions;
        _tickets = tickets;
        _codes = codes;
        _hasher = hasher;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _accessReader = accessReader;
    }

    public async Task<UseCaseResult> ExecuteAsync(CompletePasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        // §1's transport limit (at most 128 characters, checked before any lookup
        // or hashing) is normally enforced earlier, by ThrottledPasswordResetCompletion
        // over the real endpoint. This repeats it here, before any lookup, so a
        // caller that reaches this use case directly - bypassing that HTTP layer -
        // still cannot force a lookup or a hash with an oversized password. It is not
        // the §5.6 minimum-length business rule below, which stays a business rule,
        // checked last, after the user/ticket/code match.
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

        // The minimum-length rule comes last (contract §5.6): the identifier, the
        // account status and the code are all checked - and, on failure, all
        // answered with the same generic error above - before the new password's
        // own minimum length is ever considered. A valid code with a too-short
        // password leaves the ticket open: nothing below this point has run yet.
        if (request.NewPassword.Length < MinPasswordLength)
        {
            return UseCaseResult.Failure("auth.password_too_short", "The new password must be at least 8 characters.");
        }

        var newHash = _hasher.Hash(request.NewPassword);

        // actor_role_code (contract §14.1, owner decision OC-6, confirmed): this
        // endpoint is unauthenticated (§5.6) and the action needs no permission
        // (the user completes their own reset), so the rule resolves to the
        // user's first active role by ordinal code order, read fresh here since
        // no session or token carries claims to read a role from.
        var access = await _accessReader.GetAsync(user.Id, now, cancellationToken);
        var actorRoleCode = ActorRoleCodeRule.Resolve(access, requiredPermission: null);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        user.ChangePassword(newHash);
        ticket.Consume(now);
        await _sessions.RevokeAllActiveAsync(user.Id, SessionRevokeReason.PasswordResetCompleted, now, cancellationToken: cancellationToken);

        _auditWriter.Stage(AuditEvent.ForUser(
            AuditActionType.PasswordResetCompleted,
            actorUserId: user.Id,
            actorRoleCode: actorRoleCode,
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
