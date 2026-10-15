namespace Contractra.Infrastructure.Persistence.Throttling;

/// <summary>
/// The stored state of one throttle row (auth contract v1.5 §17). What the
/// values mean - window type, escalation, slots - is the Slice 6 policy.
/// </summary>
public sealed record AuthThrottleState
{
    public AuthThrottleState(DateTimeOffset windowStart, int failureCount, DateTimeOffset? blockedUntil)
    {
        EnsureUtc(windowStart, nameof(windowStart));

        if (failureCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(failureCount), failureCount,
                "A stored throttle row holds at least one counted attempt; delete the row instead.");
        }

        if (blockedUntil is { } blocked)
        {
            EnsureUtc(blocked, nameof(blockedUntil));

            if (blocked < windowStart)
            {
                throw new ArgumentException("blockedUntil must not be before windowStart.", nameof(blockedUntil));
            }
        }

        WindowStart = windowStart;
        FailureCount = failureCount;
        BlockedUntil = blockedUntil;
    }

    public DateTimeOffset WindowStart { get; }

    public int FailureCount { get; }

    public DateTimeOffset? BlockedUntil { get; }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be in UTC (offset 00:00).", parameterName);
        }
    }
}
