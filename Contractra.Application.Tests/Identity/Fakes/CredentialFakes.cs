using System.Text;
using Contractra.Application.Identity.Credentials;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity.Fakes;

/// <summary>
/// In-memory sealer for Application tests: reversible and bound to the row (a copy moved to another issuance
/// fails to open), with a switch to simulate a missing key. Not real cryptography - see AesGcmCredentialSealerTests.
/// </summary>
internal sealed class FakeCredentialSealer : ICredentialSealing, ICredentialOpening
{
    public bool Available { get; set; } = true;

    public bool KeyMissingOnOpen { get; set; }

    public bool IsAvailable => Available;

    public SealedSecret Seal(SensitivePassword password, SealContext context)
    {
        var bytes = Encoding.UTF8.GetBytes(password.Reveal()).Select(b => (byte)(b ^ 0x5A)).ToArray();
        return new SealedSecret(bytes, context.IssuanceId.ToByteArray(), "fake-key");
    }

    public SensitivePassword Open(SealedSecret sealedSecret, SealContext context)
    {
        if (KeyMissingOnOpen)
        {
            throw new SealOpenException(SealOpenFailure.KeyUnavailable);
        }

        if (!sealedSecret.Nonce.SequenceEqual(context.IssuanceId.ToByteArray()))
        {
            throw new SealOpenException(SealOpenFailure.AuthenticationFailed);
        }

        return new SensitivePassword(Encoding.UTF8.GetString(sealedSecret.Bytes.Select(b => (byte)(b ^ 0x5A)).ToArray()));
    }
}

internal sealed class StubWhatsAppSender : IWhatsAppSender
{
    private readonly Queue<WhatsAppSendResult> _script = new();
    private int _counter;

    public bool Configured { get; set; } = true;

    public bool IsConfigured => Configured;

    public List<(string Destination, string Username, string Password)> Sent { get; } = [];

    /// <summary>When set, the next send throws it (an ambiguous outcome).</summary>
    public Exception? ThrowOnNextSend { get; set; }

    public void Script(params WhatsAppSendResult[] results)
    {
        foreach (var result in results)
        {
            _script.Enqueue(result);
        }
    }

    public Task<WhatsAppSendResult> SendAsync(
        string destination, string username, SensitivePassword password, CancellationToken cancellationToken = default)
    {
        Sent.Add((destination, username, password.Reveal()));
        if (ThrowOnNextSend is { } exception)
        {
            ThrowOnNextSend = null;
            throw exception;
        }

        return Task.FromResult(_script.Count > 0
            ? _script.Dequeue()
            : new WhatsAppSendResult(WhatsAppSendOutcome.Accepted, $"wamid.{++_counter}"));
    }
}

internal sealed class FakeCredentialIssuanceStore : ICredentialIssuanceStore
{
    private readonly List<CredentialIssuance> _all = [];
    private readonly HashSet<(string, string, DateTimeOffset)> _events = [];

    public IReadOnlyList<CredentialIssuance> All => _all;

    /// <summary>User id -> time of their latest sign-in (a session created then).</summary>
    public Dictionary<Guid, DateTimeOffset> SignIns { get; } = [];

    public int SaveCount { get; private set; }

    public void Add(CredentialIssuance issuance) => _all.Add(issuance);

    public Task<CredentialIssuance?> FindOpenForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_all.SingleOrDefault(i => i.UserId == userId && i.IsOpen));

    public Task<CredentialIssuanceSummary?> FindLatestSummaryForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_all.Where(i => i.UserId == userId).OrderByDescending(i => i.IssuedAt).ThenByDescending(i => i.Id).Select(Summarize).FirstOrDefault());

    public Task<CredentialIssuance?> FindByIdAsync(Guid issuanceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_all.SingleOrDefault(i => i.Id == issuanceId));

    public Task<CredentialIssuance?> FindByProviderMessageIdAsync(string providerMessageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_all.SingleOrDefault(i => i.ProviderMessageId == providerMessageId));

    public Task<int> CountSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        Task.FromResult(_all.Count(i => i.UserId == userId && i.IssuedAt >= since));

    public Task<IReadOnlyList<CredentialIssuanceSummary>> ListSummariesForUserAsync(Guid userId, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CredentialIssuanceSummary>>(
            _all.Where(i => i.UserId == userId).OrderByDescending(i => i.IssuedAt).ThenByDescending(i => i.Id).Take(limit).Select(Summarize).ToList());

    public Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(
            _all.Where(i => i.Status is CredentialDeliveryStatus.Queued or CredentialDeliveryStatus.Sending
                    && i.NextAttemptAt <= now && (i.LeaseUntil is null || i.LeaseUntil < now))
                .Take(limit).Select(i => i.Id).ToList());

    public Task<CredentialIssuance?> TryClaimAsync(
        Guid issuanceId, string owner, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        var issuance = _all.SingleOrDefault(i => i.Id == issuanceId);
        return Task.FromResult(issuance is not null && issuance.TryClaim(owner, now, leaseDuration) ? issuance : null);
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ListOpenIdsAsync(int limit, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(_all.Where(i => i.IsOpen).Take(limit).Select(i => i.Id).ToList());

    public Task<bool> HasSignedInSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        Task.FromResult(SignIns.TryGetValue(userId, out var at) && at >= since);

    public Task<IReadOnlyList<Guid>> ListIdsWithSealedCopyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(_all.Where(i => i.HasSealedCopy).Select(i => i.Id).ToList());

    public Task<bool> TryRecordWebhookEventAsync(
        string providerMessageId, string status, DateTimeOffset providerTimestamp, string? errorCode, string? errorTitle,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Add((providerMessageId, status, providerTimestamp)));

    private static CredentialIssuanceSummary Summarize(CredentialIssuance i) =>
        new(i.Id, i.UserId, i.Kind, i.Status, i.Attempts, i.IssuedAt, i.UpdatedAt, i.Destination, i.ConsentBasis, i.FailureCategory);
}
