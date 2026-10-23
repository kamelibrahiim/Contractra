using Contractra.Application.Common;
using Contractra.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Contractra.Application.Identity.Credentials;

public enum DeliveryAttemptOutcome
{
    /// <summary>Not due, leased by another worker, or claimed first by another worker.</summary>
    NotClaimed,
    Accepted,
    WillRetry,
    Failed,
    Cancelled,
    AlreadyUsed,
}

/// <summary>
/// One delivery attempt of ADR 0007 §7: claim with a lease, re-check the issuance, open the sealed copy,
/// send, record the outcome. It never generates, hashes or stores a password and never changes the user.
/// The plaintext lives in a local variable for the send only and is never logged.
/// </summary>
public sealed class DeliverCredentialUseCase
{
    private readonly ICredentialIssuanceStore _issuances;
    private readonly IUserRepository _users;
    private readonly ICredentialOpening _opening;
    private readonly IWhatsAppSender _sender;
    private readonly CredentialDeliveryOptions _options;
    private readonly IClock _clock;
    private readonly Func<double> _random;
    private readonly ILogger<DeliverCredentialUseCase> _logger;

    public DeliverCredentialUseCase(
        ICredentialIssuanceStore issuances,
        IUserRepository users,
        ICredentialOpening opening,
        IWhatsAppSender sender,
        CredentialDeliveryOptions options,
        IClock clock,
        ILogger<DeliverCredentialUseCase>? logger = null,
        Func<double>? random = null)
    {
        _issuances = issuances;
        _users = users;
        _opening = opening;
        _sender = sender;
        _options = options;
        _clock = clock;
        _random = random ?? Random.Shared.NextDouble;
        _logger = logger ?? NullLogger<DeliverCredentialUseCase>.Instance;
    }

    public async Task<DeliveryAttemptOutcome> ExecuteAsync(
        Guid issuanceId, string workerId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var issuance = await _issuances.TryClaimAsync(issuanceId, workerId, now, _options.LeaseDuration, cancellationToken);
        if (issuance is null)
        {
            return DeliveryAttemptOutcome.NotClaimed;
        }

        // Re-check before opening the sealed copy (contract §23.4).
        if (now >= issuance.PurgeBy)
        {
            issuance.Fail(CredentialFailureCategory.ExpiredBeforeDelivery, now);
            await _issuances.SaveAsync(cancellationToken);
            return DeliveryAttemptOutcome.Failed;
        }

        var user = await _users.FindByIdAsync(issuance.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            issuance.Cancel(now);
            await _issuances.SaveAsync(cancellationToken);
            return DeliveryAttemptOutcome.Cancelled;
        }

        if (await _issuances.HasSignedInSinceAsync(issuance.UserId, issuance.IssuedAt, cancellationToken))
        {
            issuance.MarkUsed(now);
            await _issuances.SaveAsync(cancellationToken);
            return DeliveryAttemptOutcome.AlreadyUsed;
        }

        SensitivePassword password;
        try
        {
            var sealedSecret = new SealedSecret(issuance.SealedBytes!, issuance.SealNonce!, issuance.SealKeyId!);
            password = _opening.Open(sealedSecret, new SealContext(issuance.Id, issuance.UserId));
        }
        catch (SealOpenException exception)
        {
            // Key missing -> operational alert; authentication failure -> security alert. Neither retries.
            if (exception.Failure == SealOpenFailure.AuthenticationFailed)
            {
                _logger.LogError("SECURITY: a sealed credential failed authentication. IssuanceId={IssuanceId}", issuance.Id);
            }
            else
            {
                _logger.LogError("A sealed credential could not be opened: the sealing key is unavailable. IssuanceId={IssuanceId}", issuance.Id);
            }

            issuance.Fail(CredentialFailureCategory.KeyUnavailable, now);
            await _issuances.SaveAsync(cancellationToken);
            return DeliveryAttemptOutcome.Failed;
        }

        WhatsAppSendResult result;
        try
        {
            result = await _sender.SendAsync(issuance.Destination, user.LoginIdentifier.Value, password, cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // An ambiguous outcome: a retry may deliver a duplicate carrying the same, working password.
            _logger.LogWarning(
                "A credential send threw. IssuanceId={IssuanceId} ExceptionType={ExceptionType}", issuance.Id, exception.GetType().Name);
            result = new WhatsAppSendResult(WhatsAppSendOutcome.Transient, Category: CredentialFailureCategory.ProviderOutage);
        }

        var after = _clock.UtcNow;
        switch (result.Outcome)
        {
            case WhatsAppSendOutcome.Accepted:
                issuance.RecordAccepted(result.ProviderMessageId ?? $"unknown-{issuance.Id:N}", after);
                await _issuances.SaveAsync(cancellationToken);
                return DeliveryAttemptOutcome.Accepted;

            case WhatsAppSendOutcome.Transient:
                issuance.RecordTransientFailure(
                    result.Category ?? CredentialFailureCategory.ProviderOutage,
                    after,
                    after + NextDelay(issuance.Attempts, result.RetryAfter),
                    _options.MaxAttempts);
                await _issuances.SaveAsync(cancellationToken);
                return issuance.Status == CredentialDeliveryStatus.Failed ? DeliveryAttemptOutcome.Failed : DeliveryAttemptOutcome.WillRetry;

            default:
                issuance.Fail(result.Category ?? CredentialFailureCategory.TemplateUnavailable, after);
                await _issuances.SaveAsync(cancellationToken);
                return DeliveryAttemptOutcome.Failed;
        }
    }

    /// <summary>Exponential back-off with jitter from <see cref="CredentialDeliveryOptions.BackoffBase"/>; honours Retry-After.</summary>
    private TimeSpan NextDelay(int attempts, TimeSpan? retryAfter)
    {
        var exponential = _options.BackoffBase * Math.Pow(2, Math.Max(0, attempts - 1));
        var jittered = exponential * (0.5 + (0.5 * _random()));
        return retryAfter is { } wait && wait > jittered ? wait : jittered;
    }
}

/// <summary>
/// The sweeper step of ADR 0007 §6: for each open issuance, enforce the <c>PurgeBy</c> deadline and end
/// issuances whose user has already signed in. Catches anything the worker missed.
/// </summary>
public sealed class SweepCredentialsUseCase
{
    private readonly ICredentialIssuanceStore _issuances;
    private readonly IClock _clock;

    public SweepCredentialsUseCase(ICredentialIssuanceStore issuances, IClock clock)
    {
        _issuances = issuances;
        _clock = clock;
    }

    /// <returns>The number of issuances ended.</returns>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var ended = 0;
        foreach (var id in await _issuances.ListOpenIdsAsync(500, cancellationToken))
        {
            var issuance = await _issuances.FindByIdAsync(id, cancellationToken);
            if (issuance is null || !issuance.IsOpen)
            {
                continue;
            }

            if (now >= issuance.PurgeBy)
            {
                issuance.Fail(CredentialFailureCategory.ExpiredBeforeDelivery, now);
                ended++;
            }
            else if (await _issuances.HasSignedInSinceAsync(issuance.UserId, issuance.IssuedAt, cancellationToken))
            {
                issuance.MarkUsed(now);
                ended++;
            }
        }

        if (ended > 0)
        {
            await _issuances.SaveAsync(cancellationToken);
        }

        return ended;
    }
}

/// <summary>
/// Applies one provider status event (webhook, contract §23.6): idempotent on (message id, status, provider
/// timestamp), never moves a status backwards, stores only id, status, timestamp and error code/title.
/// </summary>
public sealed class ApplyWhatsAppStatusUseCase
{
    private readonly ICredentialIssuanceStore _issuances;
    private readonly IClock _clock;

    public ApplyWhatsAppStatusUseCase(ICredentialIssuanceStore issuances, IClock clock)
    {
        _issuances = issuances;
        _clock = clock;
    }

    public async Task ExecuteAsync(
        string providerMessageId, string status, DateTimeOffset providerTimestamp, string? errorCode, string? errorTitle,
        CancellationToken cancellationToken = default)
    {
        if (!TryMap(status, out var mapped))
        {
            return;
        }

        if (!await _issuances.TryRecordWebhookEventAsync(providerMessageId, status, providerTimestamp, errorCode, errorTitle, cancellationToken))
        {
            return; // a replay
        }

        var issuance = await _issuances.FindByProviderMessageIdAsync(providerMessageId, cancellationToken);
        if (issuance is null)
        {
            return; // unknown id: acknowledged, nothing to update
        }

        if (issuance.ApplyProviderStatus(mapped, providerTimestamp.ToUniversalTime(), _clock.UtcNow, CategoryFor(errorCode)))
        {
            await _issuances.SaveAsync(cancellationToken);
        }
    }

    private static bool TryMap(string status, out CredentialDeliveryStatus mapped)
    {
        switch (status)
        {
            case "sent":
                mapped = CredentialDeliveryStatus.Sent;
                return true;
            case "delivered":
                mapped = CredentialDeliveryStatus.Delivered;
                return true;
            case "read":
                mapped = CredentialDeliveryStatus.Read;
                return true;
            case "failed":
                mapped = CredentialDeliveryStatus.Failed;
                return true;
            default:
                mapped = default;
                return false;
        }
    }

    // Meta error codes are read from the documentation current at build time (PS-04b); until then a
    // small, conservative mapping: 131026 (undeliverable / not on WhatsApp), 131047/131049 (re-engagement,
    // rate), 132000-132999 (template problems), 190/10 (auth).
    private static CredentialFailureCategory CategoryFor(string? errorCode) => errorCode switch
    {
        "131026" => CredentialFailureCategory.NotOnWhatsApp,
        "131047" or "131049" or "130429" => CredentialFailureCategory.RateLimited,
        "190" or "10" => CredentialFailureCategory.AuthOrPermission,
        not null when errorCode.StartsWith("132", StringComparison.Ordinal) => CredentialFailureCategory.TemplateUnavailable,
        _ => CredentialFailureCategory.ProviderOutage,
    };
}
