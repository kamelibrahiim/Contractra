namespace Contractra.Domain.Identity;

public enum CredentialIssuanceKind
{
    Initial,
    Reset,
}

/// <summary>
/// Delivery status of an issuance (ADR 0007 §3, contract §23.4). Terminal: Delivered, Read, Failed,
/// Superseded, Cancelled. Every terminal transition purges the sealed copy.
/// </summary>
public enum CredentialDeliveryStatus
{
    Queued,
    Sending,
    Accepted,
    Sent,
    Delivered,
    Read,
    Failed,
    Unknown,
    Superseded,
    Cancelled,
}

public enum CredentialFailureCategory
{
    InvalidNumber,
    NotOnWhatsApp,
    TemplateUnavailable,
    AuthOrPermission,
    RateLimited,
    ProviderOutage,
    ConsentWithdrawn,
    KeyUnavailable,
    ExpiredBeforeDelivery,
}

/// <summary>
/// One password-issuing operation (account creation or reset/re-issue) and its WhatsApp delivery
/// (ADR 0007; owner decisions R49-R51). The password's hash lives in the user record; this row holds
/// the sealed (encrypted) copy that retries send, never plaintext. At most one issuance per user is
/// open (partial unique index). Each terminal transition nulls the sealed columns; a CHECK enforces it.
/// </summary>
public sealed class CredentialIssuance
{
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public CredentialIssuanceKind Kind { get; private set; }

    public Guid IssuedByUserId { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public CredentialDeliveryStatus Status { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public CredentialFailureCategory? FailureCategory { get; private set; }

    /// <summary>E.164 destination snapshot (the number stored for the account when issued).</summary>
    public string Destination { get; private set; } = null!;

    /// <summary>Consent evidence (OD-21): who affirmed, when, and the basis (<c>written</c> or <c>verbal</c>).</summary>
    public Guid ConsentAffirmedByUserId { get; private set; }

    public DateTimeOffset ConsentAffirmedAt { get; private set; }

    public string ConsentBasis { get; private set; } = null!;

    public int Attempts { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public string? LeaseOwner { get; private set; }

    public DateTimeOffset? LeaseUntil { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public DateTimeOffset? ProviderStatusAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    // Sealed copy (ADR 0007 §4): ciphertext||tag, nonce, key id. All null once purged.
    private byte[]? _sealedBytes;
    private byte[]? _sealNonce;

    public byte[]? SealedBytes
    {
        get => _sealedBytes is null ? null : (byte[])_sealedBytes.Clone();
        private set => _sealedBytes = value;
    }

    public byte[]? SealNonce
    {
        get => _sealNonce is null ? null : (byte[])_sealNonce.Clone();
        private set => _sealNonce = value;
    }

    public string? SealKeyId { get; private set; }

    public DateTimeOffset? SealedAt { get; private set; }

    public DateTimeOffset PurgeBy { get; private set; }

    public DateTimeOffset? PurgedAt { get; private set; }

    public bool IsOpen => !IsTerminal(Status);

    public bool HasSealedCopy => _sealedBytes is not null;

    private CredentialIssuance()
    {
    }

    public static bool IsTerminal(CredentialDeliveryStatus status) =>
        status is CredentialDeliveryStatus.Delivered or CredentialDeliveryStatus.Read
            or CredentialDeliveryStatus.Failed or CredentialDeliveryStatus.Superseded or CredentialDeliveryStatus.Cancelled;

    /// <summary>Queues a new issuance with its sealed copy (the caller has already hashed the password into the user).</summary>
    public static CredentialIssuance Queue(
        Guid id,
        Guid userId,
        CredentialIssuanceKind kind,
        Guid issuedByUserId,
        DateTimeOffset issuedAt,
        string destination,
        string consentBasis,
        byte[] sealedBytes,
        byte[] sealNonce,
        string sealKeyId,
        TimeSpan sealedCopyRetention)
    {
        if (id == Guid.Empty || userId == Guid.Empty || issuedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Issuance, user and issuer ids are required.");
        }

        EnsureUtc(issuedAt, nameof(issuedAt));

        if (string.IsNullOrWhiteSpace(destination) || destination.Length > 20)
        {
            throw new ArgumentException("A destination number is required.", nameof(destination));
        }

        if (consentBasis is not ("written" or "verbal"))
        {
            throw new ArgumentException("Consent basis must be 'written' or 'verbal'.", nameof(consentBasis));
        }

        if (sealedBytes is null || sealedBytes.Length == 0 || sealNonce is null || sealNonce.Length == 0 || string.IsNullOrEmpty(sealKeyId))
        {
            throw new ArgumentException("The sealed copy, its nonce and key id are required.");
        }

        if (sealedCopyRetention <= TimeSpan.Zero)
        {
            throw new ArgumentException("The sealed-copy retention must be positive.", nameof(sealedCopyRetention));
        }

        return new CredentialIssuance
        {
            Id = id,
            UserId = userId,
            Kind = kind,
            IssuedByUserId = issuedByUserId,
            IssuedAt = issuedAt,
            Status = CredentialDeliveryStatus.Queued,
            Destination = destination,
            ConsentAffirmedByUserId = issuedByUserId,
            ConsentAffirmedAt = issuedAt,
            ConsentBasis = consentBasis,
            Attempts = 0,
            NextAttemptAt = issuedAt,
            UpdatedAt = issuedAt,
            _sealedBytes = (byte[])sealedBytes.Clone(),
            _sealNonce = (byte[])sealNonce.Clone(),
            SealKeyId = sealKeyId,
            SealedAt = issuedAt,
            PurgeBy = issuedAt.Add(sealedCopyRetention),
        };
    }

    /// <summary>Takes the lease for one attempt; false when the row is not due or is leased by another worker.</summary>
    public bool TryClaim(string owner, DateTimeOffset now, TimeSpan leaseDuration)
    {
        EnsureUtc(now, nameof(now));
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ArgumentException("A lease owner is required.", nameof(owner));
        }

        var dueStatus = Status is CredentialDeliveryStatus.Queued or CredentialDeliveryStatus.Sending;
        if (!dueStatus || NextAttemptAt is null || NextAttemptAt > now || (LeaseUntil is { } until && until > now))
        {
            return false;
        }

        Status = CredentialDeliveryStatus.Sending;
        LeaseOwner = owner;
        LeaseUntil = now.Add(leaseDuration);
        Attempts++;
        UpdatedAt = now;
        return true;
    }

    /// <summary>The provider accepted the message. The sealed copy stays: an asynchronous failure may follow.</summary>
    public void RecordAccepted(string providerMessageId, DateTimeOffset now)
    {
        EnsureNotTerminal();
        if (string.IsNullOrWhiteSpace(providerMessageId))
        {
            throw new ArgumentException("A provider message id is required.", nameof(providerMessageId));
        }

        Status = CredentialDeliveryStatus.Accepted;
        ProviderMessageId = providerMessageId;
        ProviderStatusAt = now;
        ReleaseLease(now);
        NextAttemptAt = null;
    }

    /// <summary>A transient failure: retried with the same password until <paramref name="maxAttempts"/> is reached.</summary>
    public void RecordTransientFailure(
        CredentialFailureCategory category, DateTimeOffset now, DateTimeOffset nextAttemptAt, int maxAttempts)
    {
        EnsureNotTerminal();
        if (Attempts >= maxAttempts || nextAttemptAt >= PurgeBy)
        {
            Fail(category, now);
            return;
        }

        Status = CredentialDeliveryStatus.Queued;
        NextAttemptAt = nextAttemptAt;
        ReleaseLease(now);
    }

    /// <summary>A definitive failure (no retry) or a terminal condition: ends the issuance and purges the sealed copy.</summary>
    public void Fail(CredentialFailureCategory category, DateTimeOffset now)
    {
        EnsureNotTerminal();
        Status = CredentialDeliveryStatus.Failed;
        FailureCategory = category;
        End(now);
    }

    /// <summary>An asynchronous provider status (webhook). Never moves backwards; terminal rows ignore it.</summary>
    public bool ApplyProviderStatus(CredentialDeliveryStatus providerStatus, DateTimeOffset providerTimestamp, DateTimeOffset now, CredentialFailureCategory? failure = null)
    {
        if (IsTerminal(Status) || Rank(providerStatus) <= Rank(Status) && providerStatus != CredentialDeliveryStatus.Failed)
        {
            return false;
        }

        switch (providerStatus)
        {
            case CredentialDeliveryStatus.Sent:
                Status = CredentialDeliveryStatus.Sent;
                ProviderStatusAt = providerTimestamp;
                UpdatedAt = now;
                return true;
            case CredentialDeliveryStatus.Delivered or CredentialDeliveryStatus.Read:
                Status = providerStatus;
                ProviderStatusAt = providerTimestamp;
                End(now);
                return true;
            case CredentialDeliveryStatus.Failed:
                Status = CredentialDeliveryStatus.Failed;
                FailureCategory = failure ?? CredentialFailureCategory.ProviderOutage;
                ProviderStatusAt = providerTimestamp;
                End(now);
                return true;
            default:
                return false;
        }
    }

    /// <summary>A newer operation replaced this one; its password no longer works.</summary>
    public void Supersede(DateTimeOffset now)
    {
        EnsureNotTerminal();
        Status = CredentialDeliveryStatus.Superseded;
        End(now);
    }

    /// <summary>The account was disabled (or the employee already signed in/changed the password).</summary>
    public void Cancel(DateTimeOffset now)
    {
        EnsureNotTerminal();
        Status = CredentialDeliveryStatus.Cancelled;
        End(now);
    }

    /// <summary>The employee signed in or changed the password: the sealed copy is no longer needed.</summary>
    public void MarkUsed(DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return;
        }

        Status = CredentialDeliveryStatus.Delivered;
        End(now);
    }

    /// <summary>Nulls the sealed columns. Idempotent.</summary>
    public void PurgeSealedCopy(DateTimeOffset now)
    {
        if (!HasSealedCopy)
        {
            return;
        }

        _sealedBytes = null;
        _sealNonce = null;
        SealKeyId = null;
        PurgedAt = now;
        UpdatedAt = now;
    }

    private void End(DateTimeOffset now)
    {
        EndedAt = now;
        NextAttemptAt = null;
        ReleaseLease(now);
        PurgeSealedCopy(now);
    }

    private void ReleaseLease(DateTimeOffset now)
    {
        LeaseOwner = null;
        LeaseUntil = null;
        UpdatedAt = now;
    }

    private void EnsureNotTerminal()
    {
        if (IsTerminal(Status))
        {
            throw new InvalidOperationException("This credential issuance has ended.");
        }
    }

    private static int Rank(CredentialDeliveryStatus status) => status switch
    {
        CredentialDeliveryStatus.Queued => 0,
        CredentialDeliveryStatus.Sending => 1,
        CredentialDeliveryStatus.Accepted => 2,
        CredentialDeliveryStatus.Sent => 3,
        CredentialDeliveryStatus.Delivered => 4,
        CredentialDeliveryStatus.Read => 5,
        _ => 9,
    };

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be in UTC (offset 00:00).", parameterName);
        }
    }
}
