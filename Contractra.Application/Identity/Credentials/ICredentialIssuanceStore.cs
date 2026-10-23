using Contractra.Domain.Identity;

namespace Contractra.Application.Identity.Credentials;

/// <summary>An issuance without its sealed columns: what the request path and the Admin screens may read.</summary>
public sealed record CredentialIssuanceSummary(
    Guid Id,
    Guid UserId,
    CredentialIssuanceKind Kind,
    CredentialDeliveryStatus Status,
    int Attempts,
    DateTimeOffset IssuedAt,
    DateTimeOffset UpdatedAt,
    string Destination,
    string ConsentBasis,
    CredentialFailureCategory? FailureCategory)
{
    public bool IsOpen => !CredentialIssuance.IsTerminal(Status);

    public CredentialDeliveryView ToView(DateTimeOffset now, TimeSpan stalledAfter) =>
        new(Id, Kind, Status, Attempts, UpdatedAt, WhatsAppNumber.Mask(Destination), FailureCategory,
            Status is CredentialDeliveryStatus.Queued or CredentialDeliveryStatus.Sending && now - UpdatedAt > stalledAfter);
}

/// <summary>
/// Persistence for <see cref="CredentialIssuance"/>. Tracked results are saved through the caller's
/// unit of work or <see cref="SaveAsync"/>. Reads that return a <see cref="CredentialIssuanceSummary"/> project
/// explicit columns and never select the sealed ones; the entity reads (<see cref="FindByIdAsync"/>,
/// <see cref="FindOpenForUserAsync"/>, <see cref="TryClaimAsync"/>) load the row so it can be changed - the
/// request path loads the open row only to purge its sealed copy (ADR 0007 §5).
/// </summary>
public interface ICredentialIssuanceStore
{
    void Add(CredentialIssuance issuance);

    /// <summary>The user's open issuance (tracked), or null.</summary>
    Task<CredentialIssuance?> FindOpenForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The user's latest issuance by <c>IssuedAt</c>, or null. No sealed columns.</summary>
    Task<CredentialIssuanceSummary?> FindLatestSummaryForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<CredentialIssuance?> FindByIdAsync(Guid issuanceId, CancellationToken cancellationToken = default);

    Task<CredentialIssuance?> FindByProviderMessageIdAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>Issuance count for the user since <paramref name="since"/> (the daily re-issue cap).</summary>
    Task<int> CountSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>History for the status endpoint, newest first. No sealed columns.</summary>
    Task<IReadOnlyList<CredentialIssuanceSummary>> ListSummariesForUserAsync(Guid userId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Ids of open issuances that are due and not leased, in due order.</summary>
    Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims the issuance for one attempt with a lease and saves it, or returns null when it is not due, is
    /// leased, or another worker claimed it first (optimistic concurrency). The returned row is tracked.
    /// </summary>
    Task<CredentialIssuance?> TryClaimAsync(
        Guid issuanceId, string owner, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    /// <summary>Saves changes to tracked issuances outside a use-case transaction (worker and sweeper steps).</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Open issuances (the sweeper checks each against its deadline and sign-in state).</summary>
    Task<IReadOnlyList<Guid>> ListOpenIdsAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>True when the user has a session created at or after <paramref name="since"/> (a sign-in after the issuance).</summary>
    Task<bool> HasSignedInSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>Every issuance still holding a sealed copy (emergency purge).</summary>
    Task<IReadOnlyList<Guid>> ListIdsWithSealedCopyAsync(CancellationToken cancellationToken = default);

    /// <summary>Records one webhook status event; false when (message id, status, provider timestamp) was already stored.</summary>
    Task<bool> TryRecordWebhookEventAsync(
        string providerMessageId, string status, DateTimeOffset providerTimestamp, string? errorCode, string? errorTitle,
        CancellationToken cancellationToken = default);
}
