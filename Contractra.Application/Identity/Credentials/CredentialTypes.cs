using Contractra.Domain.Identity;

namespace Contractra.Application.Identity.Credentials;

/// <summary>
/// A generated password in memory (ADR 0007 §4). It has no <c>ToString</c> that shows the value,
/// no serialization and no value equality, so it cannot reach a log template, DTO or ProblemDetails
/// by accident. <see cref="Reveal"/> is called only to hash, seal and send.
/// </summary>
public sealed class SensitivePassword
{
    private readonly string _value;

    public SensitivePassword(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        _value = value;
    }

    public string Reveal() => _value;

    public override string ToString() => "[redacted password]";

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}

/// <summary>An encrypted password: ciphertext with tag, nonce and key id. Its text form hides the bytes.</summary>
public sealed record SealedSecret(byte[] Bytes, byte[] Nonce, string KeyId)
{
    public override string ToString() => $"[sealed secret key={KeyId}]";
}

/// <summary>The row a sealed copy is bound to (associated data): a copy moved elsewhere fails to open.</summary>
public readonly record struct SealContext(Guid IssuanceId, Guid UserId);

/// <summary>Request-path view of the sealer: it can seal but never open (ADR 0007 §4).</summary>
public interface ICredentialSealing
{
    /// <summary>False when no active sealing key is configured; create and reset then answer 503.</summary>
    bool IsAvailable { get; }

    SealedSecret Seal(SensitivePassword password, SealContext context);
}

/// <summary>Worker-only view: opens a sealed copy when about to send.</summary>
public interface ICredentialOpening
{
    /// <exception cref="SealOpenException">The key is missing or the copy fails authentication.</exception>
    SensitivePassword Open(SealedSecret sealedSecret, SealContext context);
}

public enum SealOpenFailure
{
    KeyUnavailable,
    AuthenticationFailed,
}

public sealed class SealOpenException : Exception
{
    public SealOpenFailure Failure { get; }

    public SealOpenException(SealOpenFailure failure)
        : base($"The sealed credential could not be opened ({failure}).")
    {
        Failure = failure;
    }
}

public enum WhatsAppSendOutcome
{
    Accepted,
    Transient,
    Definitive,
}

/// <param name="Category">The failure category for <see cref="WhatsAppSendOutcome.Transient"/> and <see cref="WhatsAppSendOutcome.Definitive"/>.</param>
/// <param name="RetryAfter">The provider's <c>Retry-After</c>, when it gave one.</param>
public sealed record WhatsAppSendResult(
    WhatsAppSendOutcome Outcome,
    string? ProviderMessageId = null,
    CredentialFailureCategory? Category = null,
    TimeSpan? RetryAfter = null);

/// <summary>
/// Sends the username and password in the approved template (ADR 0007 §7). The plaintext is never
/// logged by any implementation. The live Meta Cloud API sender is PS-04b; PS-04a ships the fake.
/// </summary>
public interface IWhatsAppSender
{
    /// <summary>Whether a sender is configured; false answers 503 on create and reset.</summary>
    bool IsConfigured { get; }

    Task<WhatsAppSendResult> SendAsync(
        string destination, string username, SensitivePassword password, CancellationToken cancellationToken = default);
}

/// <summary>Delivery configuration (ADR 0007; defaults are the provisional OD-18 values).</summary>
public sealed class CredentialDeliveryOptions
{
    public const string Section = "WhatsApp";

    public bool Enabled { get; set; }

    public TimeSpan SealedCopyRetention { get; set; } = TimeSpan.FromHours(24);

    public int MaxAttempts { get; set; } = 5;

    public TimeSpan BackoffBase { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan MinReissueInterval { get; set; } = TimeSpan.FromSeconds(60);

    public int MaxReissuesPer24Hours { get; set; } = 5;

    public TimeSpan StalledAfter { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan WorkerPollInterval { get; set; } = TimeSpan.FromSeconds(5);
}
