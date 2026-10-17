namespace Contractra.Infrastructure.Persistence.Throttling;

/// <summary>One layer's decision for one attempt: admitted (with the row's next state) or rejected.</summary>
public readonly record struct AuthThrottleStep(bool Admitted, TimeSpan RetryAfter, AuthThrottleState? Next, bool EnteredSlowMode = false)
{
    public static AuthThrottleStep Admit(AuthThrottleState next, bool enteredSlowMode = false) => new(true, TimeSpan.Zero, next, enteredSlowMode);

    public static AuthThrottleStep Reject(TimeSpan retryAfter) => new(false, retryAfter, null);
}

/// <summary>
/// The contract v1.5 §17 policy for the persisted layers, as pure functions of the
/// stored row (the five §17 columns) and the time.
///
/// Interpretation of §17 recorded here (Slice 6):
/// - Windows are anchored: <c>window_start</c> is the first counted attempt; the
///   stored columns (§17: window_start, failure_count, blocked_until) hold no per-attempt
///   history, so a sliding window is not representable.
/// - Every admitted attempt is counted at admission; a success releases it (layer 3,
///   layer 4) or clears the counter (layer 2); any other outcome stays counted
///   (§22 D, consistent with §17's "failures"). Rejected attempts are never counted.
/// - Layer 2 escalation: once the limit is reached each admitted attempt blocks the
///   pair for base × 2^(n − limit), capped (base 30 s per §22 B; §17 gives none), and
///   moves window_start to that attempt; escalation ends once more than a full window
///   passes without an attempt, or on a success.
/// - Layer 3 slow mode: entered when the count reaches the limit; then every admitted
///   attempt - from any address, successful or not - takes the one slot (§17 "at most
///   one attempt per 30 seconds"; §22 C); it ends once more than a full window passes
///   after the last attempt.
/// - Layer 4: the limit, then 429 until the window passes; the session is untouched.
/// </summary>
public static class AuthThrottlePolicy
{
    private const int MaxEscalationSteps = 32;

    // ---- Layer 2: (identifier key, client IP) ----

    public static AuthThrottleStep AdmitPair(AuthThrottleState? state, DateTimeOffset now, AuthThrottleOptions options)
    {
        var limit = options.PairFailureLimit;
        if (state is null || PairExpired(state, now, options))
        {
            return AuthThrottleStep.Admit(1 >= limit
                ? new AuthThrottleState(now, 1, now + PairDelay(1, options))
                : new AuthThrottleState(now, 1, null));
        }

        if (state.FailureCount >= limit && state.BlockedUntil is { } blocked && blocked > now)
        {
            return AuthThrottleStep.Reject(blocked - now);
        }

        var count = Math.Min(state.FailureCount + 1, limit + MaxEscalationSteps);
        return AuthThrottleStep.Admit(count >= limit
            ? new AuthThrottleState(now, count, now + PairDelay(count, options))
            : new AuthThrottleState(state.WindowStart, count, null));
    }

    public static TimeSpan PairDelay(int count, AuthThrottleOptions options)
    {
        var exponent = Math.Clamp(count - options.PairFailureLimit, 0, MaxEscalationSteps);
        var ticks = options.PairBaseDelay.Ticks * Math.Pow(2, exponent);
        return ticks >= options.PairMaxDelay.Ticks ? options.PairMaxDelay : TimeSpan.FromTicks((long)ticks);
    }

    /// <summary>
    /// Below the limit: the anchored window has passed. In escalation: more than a full
    /// window since the last admitted attempt (strictly, so an attempt made exactly when
    /// a capped 15-minute block ends keeps the escalation - 4 per hour, §22 B).
    /// </summary>
    private static bool PairExpired(AuthThrottleState state, DateTimeOffset now, AuthThrottleOptions options) =>
        state.FailureCount < options.PairFailureLimit
            ? now >= state.WindowStart + options.PairWindow
            : now > state.WindowStart + options.PairWindow && (state.BlockedUntil is null || now >= state.BlockedUntil);

    // ---- Layer 3: identifier key, all IPs ----

    public static AuthThrottleStep AdmitIdentifier(AuthThrottleState? state, DateTimeOffset now, AuthThrottleOptions options)
    {
        var limit = options.IdentifierFailureLimit;
        var slow = IsSlowMode(state, options);

        if (state is null
            || (slow
                ? now > state.WindowStart + options.IdentifierWindow && now >= state.BlockedUntil
                : now >= state.WindowStart + options.IdentifierWindow))
        {
            return 1 >= limit
                ? AuthThrottleStep.Admit(new AuthThrottleState(now, 1, now + options.IdentifierSlot), enteredSlowMode: true)
                : AuthThrottleStep.Admit(new AuthThrottleState(now, 1, null));
        }

        if (slow)
        {
            if (state.BlockedUntil is { } next && next > now)
            {
                return AuthThrottleStep.Reject(next - now);
            }

            return AuthThrottleStep.Admit(new AuthThrottleState(now, limit, now + options.IdentifierSlot));
        }

        var count = state.FailureCount + 1;
        return count >= limit
            ? AuthThrottleStep.Admit(new AuthThrottleState(now, count, now + options.IdentifierSlot), enteredSlowMode: true)
            : AuthThrottleStep.Admit(new AuthThrottleState(state.WindowStart, count, null));
    }

    /// <summary>
    /// A success releases its own count. In slow mode a success keeps the mode and its
    /// slot (contract §17 test list: a success clears only layer 2) - unless this very
    /// attempt entered slow mode and no attempt followed it, in which case the mode is undone.
    /// Returns <c>(changed, next)</c>; next = null deletes the row.
    /// </summary>
    public static (bool Changed, AuthThrottleState? Next) ReleaseIdentifier(
        AuthThrottleState? state, DateTimeOffset admittedAt, bool enteredSlowMode, AuthThrottleOptions options)
    {
        if (state is null)
        {
            return (false, null);
        }

        var limit = options.IdentifierFailureLimit;
        if (enteredSlowMode)
        {
            if (IsSlowMode(state, options) && state.WindowStart == admittedAt)
            {
                return limit - 1 < 1 ? (true, null) : (true, new AuthThrottleState(state.WindowStart, limit - 1, null));
            }

            return (false, null);
        }

        if (!IsSlowMode(state, options) && state.WindowStart <= admittedAt)
        {
            var count = state.FailureCount - 1;
            return count < 1 ? (true, null) : (true, new AuthThrottleState(state.WindowStart, count, null));
        }

        return (false, null);
    }

    private static bool IsSlowMode(AuthThrottleState? state, AuthThrottleOptions options) =>
        state is not null && state.FailureCount >= options.IdentifierFailureLimit && state.BlockedUntil is not null;

    // ---- Layer 4: user, wrong current password on change-password ----

    public static AuthThrottleStep AdmitChangePassword(AuthThrottleState? state, DateTimeOffset now, AuthThrottleOptions options)
    {
        var window = options.ChangePasswordWindow;
        if (state is null || now >= state.WindowStart + window)
        {
            return AuthThrottleStep.Admit(new AuthThrottleState(now, 1, null));
        }

        if (state.FailureCount >= options.ChangePasswordFailureLimit)
        {
            return AuthThrottleStep.Reject(state.WindowStart + window - now);
        }

        return AuthThrottleStep.Admit(new AuthThrottleState(state.WindowStart, state.FailureCount + 1, null));
    }

    public static (bool Changed, AuthThrottleState? Next) ReleaseChangePassword(AuthThrottleState? state, DateTimeOffset admittedAt)
    {
        if (state is null || state.WindowStart > admittedAt)
        {
            return (false, null);
        }

        var count = state.FailureCount - 1;
        return count < 1 ? (true, null) : (true, new AuthThrottleState(state.WindowStart, count, null));
    }
}
