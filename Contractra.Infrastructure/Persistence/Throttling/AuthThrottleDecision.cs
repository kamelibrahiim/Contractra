namespace Contractra.Infrastructure.Persistence.Throttling;

/// <summary>
/// What a throttle step decided: the result for the caller, and the new state
/// of each row it changes (null = delete the row). Rows not listed are left as
/// they are.
/// </summary>
public sealed class AuthThrottleDecision<TResult>
{
    private static readonly IReadOnlyDictionary<AuthThrottleKey, AuthThrottleState?> NoChanges =
        new Dictionary<AuthThrottleKey, AuthThrottleState?>();

    public AuthThrottleDecision(TResult result, IReadOnlyDictionary<AuthThrottleKey, AuthThrottleState?>? changes = null)
    {
        Result = result;
        Changes = changes ?? NoChanges;
    }

    public TResult Result { get; }

    public IReadOnlyDictionary<AuthThrottleKey, AuthThrottleState?> Changes { get; }
}
