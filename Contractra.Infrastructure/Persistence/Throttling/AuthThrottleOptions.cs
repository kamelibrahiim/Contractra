namespace Contractra.Infrastructure.Persistence.Throttling;

/// <summary>
/// Contract v1.5 §17 limits, configuration <c>Auth:Throttle:*</c>; the defaults are
/// the contract's. Layers 1 and 5 are the per-instance ASP.NET Core rate limiter;
/// layers 2-4 are the shared store.
/// </summary>
public sealed class AuthThrottleOptions
{
    /// <summary>Layer 1: requests per client IP to all public auth endpoints, per window.</summary>
    public int PublicPermitLimit { get; set; } = 30;

    public TimeSpan PublicWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Layer 5: refresh requests per client IP, per window.</summary>
    public int RefreshPermitLimit { get; set; } = 60;

    public TimeSpan RefreshWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Layer 2: failures per (identifier key, client IP) before 429.</summary>
    public int PairFailureLimit { get; set; } = 5;

    public TimeSpan PairWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Layer 2: first delay when the limit is reached; it doubles with each further
    /// failure up to <see cref="PairMaxDelay"/>. §17 does not state the base; 30 seconds
    /// is the value proposed in contract §22 B.
    /// </summary>
    public TimeSpan PairBaseDelay { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan PairMaxDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Layer 3: failures per identifier key (all IPs) before slow mode.</summary>
    public int IdentifierFailureLimit { get; set; } = 10;

    public TimeSpan IdentifierWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Layer 3: in slow mode, at most one attempt per slot from any address.</summary>
    public TimeSpan IdentifierSlot { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Layer 4: wrong current passwords per user before 429.</summary>
    public int ChangePasswordFailureLimit { get; set; } = 5;

    public TimeSpan ChangePasswordWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a throttle step waits for a row lock before failing (fail closed).</summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public bool CleanupEnabled { get; set; } = true;

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    public int CleanupBatchSize { get; set; } = 1000;

    /// <summary>The window of each persisted scope, for the periodic cleanup.</summary>
    public IReadOnlyDictionary<string, TimeSpan> WindowByScope => new Dictionary<string, TimeSpan>
    {
        [AuthThrottleScopes.LoginPair] = PairWindow,
        [AuthThrottleScopes.Identifier] = IdentifierWindow,
        [AuthThrottleScopes.ChangePasswordUser] = ChangePasswordWindow,
    };

    /// <summary>Throws when a value is unusable, so startup fails rather than running unthrottled.</summary>
    public void Validate()
    {
        static void Positive(int value, string name)
        {
            if (value < 1)
            {
                throw new InvalidOperationException($"Auth:Throttle:{name} must be at least 1.");
            }
        }

        static void PositiveTime(TimeSpan value, string name)
        {
            if (value <= TimeSpan.Zero)
            {
                throw new InvalidOperationException($"Auth:Throttle:{name} must be positive.");
            }
        }

        Positive(PublicPermitLimit, nameof(PublicPermitLimit));
        Positive(RefreshPermitLimit, nameof(RefreshPermitLimit));
        Positive(PairFailureLimit, nameof(PairFailureLimit));
        Positive(IdentifierFailureLimit, nameof(IdentifierFailureLimit));
        Positive(ChangePasswordFailureLimit, nameof(ChangePasswordFailureLimit));
        Positive(CleanupBatchSize, nameof(CleanupBatchSize));
        PositiveTime(PublicWindow, nameof(PublicWindow));
        PositiveTime(RefreshWindow, nameof(RefreshWindow));
        PositiveTime(PairWindow, nameof(PairWindow));
        PositiveTime(PairBaseDelay, nameof(PairBaseDelay));
        PositiveTime(PairMaxDelay, nameof(PairMaxDelay));
        PositiveTime(IdentifierWindow, nameof(IdentifierWindow));
        PositiveTime(IdentifierSlot, nameof(IdentifierSlot));
        PositiveTime(ChangePasswordWindow, nameof(ChangePasswordWindow));
        PositiveTime(CleanupInterval, nameof(CleanupInterval));

        if (LockTimeout <= TimeSpan.Zero || LockTimeout > AuthThrottleStore.MaxLockTimeout)
        {
            throw new InvalidOperationException($"Auth:Throttle:LockTimeout must be positive and at most {AuthThrottleStore.MaxLockTimeout}.");
        }

        if (CleanupBatchSize > AuthThrottleStore.MaxBatchSize)
        {
            throw new InvalidOperationException($"Auth:Throttle:CleanupBatchSize must be at most {AuthThrottleStore.MaxBatchSize}.");
        }
    }
}
