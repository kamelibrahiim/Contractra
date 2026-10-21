using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Application.Identity.Auth;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Application.Identity.UseCases;

/// <summary>
/// Auth contract v1.5 §9.6, §10A, owner decisions R15, R20, R27, R43, R44;
/// engineering decisions E1, E3. The Admin never learns the code: it is
/// handed to the configured delivery channel only after the transaction
/// commits. Fails closed with 503 when no channel is registered, per the
/// residual-risk note in §10A - no channel exists in Sprint 2, so this is
/// the response for every account until one is built.
///
/// After the commit the code is handed to the channel. Per §9.6 step 6 a
/// delivery failure is logged and leaves the committed ticket valid until it
/// expires (the Admin may issue a new reset, which supersedes it). The
/// failure is neither rethrown nor reported as a delivered code: the result
/// carries <see cref="IssuePasswordResetResult.DeliveryFailed"/> so the caller
/// (Slice 6/7) can map it. §9.6 does not say which HTTP status that maps to.
/// </summary>
public sealed class IssuePasswordResetUseCase
{
    private readonly IUserRepository _users;
    private readonly ISessionStore _sessions;
    private readonly IPasswordResetTicketStore _tickets;
    private readonly IResetCodeGenerator _codes;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReadOnlyList<IPasswordResetDelivery> _deliveryChannels;
    private readonly TimeSpan _codeLifetime;
    private readonly ILogger<IssuePasswordResetUseCase> _logger;
    private readonly IUserAccessReader _accessReader;

    public IssuePasswordResetUseCase(
        IUserRepository users,
        ISessionStore sessions,
        IPasswordResetTicketStore tickets,
        IResetCodeGenerator codes,
        IAuditWriter auditWriter,
        IClock clock,
        IUnitOfWork unitOfWork,
        IEnumerable<IPasswordResetDelivery> deliveryChannels,
        IUserAccessReader accessReader,
        TimeSpan? codeLifetime = null,
        ILogger<IssuePasswordResetUseCase>? logger = null)
    {
        _users = users;
        _sessions = sessions;
        _tickets = tickets;
        _codes = codes;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _deliveryChannels = deliveryChannels.ToList();
        _accessReader = accessReader;
        _codeLifetime = codeLifetime ?? TimeSpan.FromHours(24);
        _logger = logger ?? NullLogger<IssuePasswordResetUseCase>.Instance;
    }

    public async Task<UseCaseResult<IssuePasswordResetResult>> ExecuteAsync(
        IssuePasswordResetRequest request, CancellationToken cancellationToken = default)
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
            return Fail("users.already_disabled", "Reactivate the user before issuing a reset.");
        }

        if (user.MustChangePassword)
        {
            return Fail("users.initial_password_pending", "This account is still on its initial temporary password; re-issue it instead.");
        }

        var channel = await FindReachableChannelAsync(user.Id, cancellationToken);
        if (channel is null)
        {
            return Fail("users.reset_delivery_unavailable", "No password-reset delivery channel is available.");
        }

        var now = _clock.UtcNow;
        var generatedCode = _codes.Generate();
        var expiresAt = now.Add(_codeLifetime);
        var ticket = PasswordResetTicket.Issue(user.Id, generatedCode.Hash, now, expiresAt, request.ActorUserId, channel.Channel);

        // actor_role_code (contract §14.1, owner decision OC-6, confirmed): this
        // action requires Users.Manage, so the rule resolves to the active role
        // through which the actor held it (several → first by ordinal code order).
        var access = await _accessReader.GetAsync(request.ActorUserId, now, cancellationToken);
        var actorRoleCode = ActorRoleCodeRule.Resolve(access, "Users.Manage");

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        user.BeginPasswordReset();
        await _tickets.IssueAsync(ticket, cancellationToken);
        await _sessions.RevokeAllActiveAsync(user.Id, SessionRevokeReason.PasswordResetIssued, now, cancellationToken: cancellationToken);

        _auditWriter.Stage(AuditEvent.ForUser(
            AuditActionType.PasswordResetIssued,
            actorUserId: request.ActorUserId,
            actorRoleCode: actorRoleCode,
            entityType: "User",
            entityId: user.Id,
            occurredAtUtc: now,
            correlationId: request.CorrelationId,
            source: request.Source,
            snapshot: new Dictionary<string, string?>
            {
                ["password_reset_ticket_id"] = ticket.Id.ToString(),
                ["issued_by_user_id"] = request.ActorUserId.ToString(),
                ["channel"] = channel.Channel,
                ["issued_at"] = now.ToString("O"),
                ["expires_at"] = expiresAt.ToString("O"),
                ["consumed_at"] = null,
            }));

        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);

        // The transaction has committed: nothing below can undo the ticket.
        var deliveryFailed = false;
        try
        {
            await channel.SendAsync(user.Id, generatedCode.Code, cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            deliveryFailed = true;

            // Never the code, and not the exception object either: a channel's
            // message or stack could carry the code it was asked to send.
            _logger.LogWarning(
                "Password-reset delivery failed after commit. CorrelationId={CorrelationId} UserId={UserId} TicketId={TicketId} Channel={Channel} ExceptionType={ExceptionType}",
                request.CorrelationId, user.Id, ticket.Id, channel.Channel, exception.GetType().Name);
        }

        return UseCaseResult<IssuePasswordResetResult>.Success(
            new IssuePasswordResetResult(channel.Channel, expiresAt, deliveryFailed));
    }

    private async Task<IPasswordResetDelivery?> FindReachableChannelAsync(Guid userId, CancellationToken cancellationToken)
    {
        foreach (var channel in _deliveryChannels)
        {
            if (await channel.CanReachAsync(userId, cancellationToken))
            {
                return channel;
            }
        }

        return null;
    }

    private static UseCaseResult<IssuePasswordResetResult> Fail(string code, string message) =>
        UseCaseResult<IssuePasswordResetResult>.Failure(code, message);
}
