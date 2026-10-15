namespace Contractra.Infrastructure.Persistence.Throttling;

/// <summary>
/// One row of the shared brute-force throttle store (auth contract v1.5 §17;
/// ADR 0005 §3.4): the five columns the contract allows, nothing that
/// identifies a user or an address in clear. Read and written only through
/// <see cref="AuthThrottleStore"/>; mapped so the schema lives in the EF model
/// and its migrations.
/// </summary>
public sealed class AuthThrottleCounter
{
    public string Scope { get; private set; } = null!;

    /// <summary>SHA-256 of the throttle key (for example the lower-cased login identifier).</summary>
    public byte[] KeyHash { get; private set; } = null!;

    public DateTimeOffset WindowStart { get; private set; }

    public int FailureCount { get; private set; }

    public DateTimeOffset? BlockedUntil { get; private set; }

    private AuthThrottleCounter()
    {
    }
}
