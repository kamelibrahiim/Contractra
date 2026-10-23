using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Application.Identity.Auth;
using Contractra.Application.Identity.Credentials;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Identity.UseCases;

/// <summary>Auth contract v1.6 §23.3. <paramref name="ExpectedIssuanceId"/> is required when the account has an open or failed latest issuance.</summary>
public sealed record ReissueCredentialRequest(
    Guid TargetUserId,
    Guid? ExpectedIssuanceId,
    Guid ActorUserId,
    string CorrelationId,
    AuditSource Source);

/// <summary>
/// Auth contract v1.6 §23.3, owner decisions R50, R51, roadmap OD-1/OD-29 (provisional); ADR 0007. A reset
/// and a re-issue are the same operation: the system generates one new password, replaces the user's hash,
/// revokes every session, supersedes the earlier open issuance (purging its sealed copy) and queues the new
/// delivery to the number already stored for the account - all in one transaction. The request cannot name a
/// destination, and the password is never returned.
/// </summary>
public sealed class ReissueCredentialUseCase
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ITemporaryPasswordGenerator _passwords;
    private readonly ICredentialSealing _sealing;
    private readonly IWhatsAppSender _sender;
    private readonly ICredentialIssuanceStore _issuances;
    private readonly CredentialDeliveryOptions _delivery;
    private readonly ISessionStore _sessions;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserAccessReader _accessReader;

    public ReissueCredentialUseCase(
        IUserRepository users,
        IPasswordHasher hasher,
        ITemporaryPasswordGenerator passwords,
        ICredentialSealing sealing,
        IWhatsAppSender sender,
        ICredentialIssuanceStore issuances,
        CredentialDeliveryOptions delivery,
        ISessionStore sessions,
        IAuditWriter auditWriter,
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserAccessReader accessReader)
    {
        _users = users;
        _hasher = hasher;
        _passwords = passwords;
        _sealing = sealing;
        _sender = sender;
        _issuances = issuances;
        _delivery = delivery;
        _sessions = sessions;
        _auditWriter = auditWriter;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _accessReader = accessReader;
    }

    public async Task<UseCaseResult<CredentialDeliveryView>> ExecuteAsync(
        ReissueCredentialRequest request, CancellationToken cancellationToken = default)
    {
        // Contract §9 check order, steps 5-7.
        var user = await _users.FindByIdAsync(request.TargetUserId, cancellationToken);
        if (user is null)
        {
            return Fail(UserErrorCodes.NotFound, "The user does not exist.");
        }

        if (user.IsBootstrapAdmin)
        {
            return Fail(UserErrorCodes.BootstrapAccountProtected, "The bootstrap admin is protected from this action.");
        }

        if (user.Status != UserStatus.Active)
        {
            return Fail(UserErrorCodes.AlreadyDisabled, "Reactivate the user before issuing a new credential.");
        }

        // OD-1(b), provisional: self-service is change-password, not an Admin reset.
        if (user.Id == request.ActorUserId)
        {
            return Fail(UserErrorCodes.SelfResetNotAllowed, "Use change-password for your own account.");
        }

        if (!_delivery.Enabled || !_sender.IsConfigured || !_sealing.IsAvailable)
        {
            return Fail(UserErrorCodes.CredentialDeliveryUnavailable, "Credential delivery is not available; nothing changed.");
        }

        var latest = await _issuances.FindLatestSummaryForUserAsync(user.Id, cancellationToken);
        if (latest is null)
        {
            return Fail(UserErrorCodes.WhatsAppDestinationMissing, "The account has no stored WhatsApp number with recorded consent.");
        }

        // A double click or a stale screen must not issue twice (contract §23.3).
        var latestNeedsToken = latest.IsOpen || latest.Status == CredentialDeliveryStatus.Failed;
        if (latestNeedsToken && request.ExpectedIssuanceId != latest.Id)
        {
            return Fail(UserErrorCodes.IssuanceChanged, "The account's credential state changed; reload and try again.");
        }

        var now = _clock.UtcNow;
        if (latest.IssuedAt + _delivery.MinReissueInterval > now)
        {
            var wait = (int)Math.Ceiling((latest.IssuedAt + _delivery.MinReissueInterval - now).TotalSeconds);
            return Fail(UserErrorCodes.ReissueTooSoon, wait.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (await _issuances.CountSinceAsync(user.Id, now.AddHours(-24), cancellationToken) >= _delivery.MaxReissuesPer24Hours)
        {
            return Fail(UserErrorCodes.ReissueTooSoon, ((int)TimeSpan.FromHours(1).TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var password = new SensitivePassword(_passwords.Generate());
        var issuanceId = Guid.CreateVersion7();
        var sealedSecret = _sealing.Seal(password, new SealContext(issuanceId, user.Id));
        var issuance = CredentialIssuance.Queue(
            issuanceId, user.Id, CredentialIssuanceKind.Reset, request.ActorUserId, now,
            latest.Destination, latest.ConsentBasis, sealedSecret.Bytes, sealedSecret.Nonce, sealedSecret.KeyId,
            _delivery.SealedCopyRetention);

        var access = await _accessReader.GetAsync(request.ActorUserId, now, cancellationToken);
        var actorRoleCode = ActorRoleCodeRule.Resolve(access, "Users.Manage");

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        // OD-29(a), provisional: the old password and every session end at request time.
        user.ReplacePasswordForIssuance(_hasher.Hash(password.Reveal()));
        await _sessions.RevokeAllActiveAsync(user.Id, SessionRevokeReason.PasswordResetIssued, now, cancellationToken: cancellationToken);

        var open = await _issuances.FindOpenForUserAsync(user.Id, cancellationToken);
        open?.Supersede(now);
        _issuances.Add(issuance);

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
                ["credential_issuance_id"] = issuance.Id.ToString(),
                ["issued_by_user_id"] = request.ActorUserId.ToString(),
                ["channel"] = "whatsapp",
                ["issued_at"] = now.ToString("O"),
            }));

        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);

        return UseCaseResult<CredentialDeliveryView>.Success(CredentialDeliveryView.From(issuance, now, _delivery.StalledAfter));
    }

    private static UseCaseResult<CredentialDeliveryView> Fail(string code, string message) =>
        UseCaseResult<CredentialDeliveryView>.Failure(code, message);
}
