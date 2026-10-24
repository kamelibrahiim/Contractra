using Contractra.Infrastructure.Persistence.Throttling;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// The contract v1.5 §17 policy as pure functions (no database): thresholds, the doubling
/// delay and its cap, anchored windows, layer 3 slow mode and its slot, layer 4.
/// </summary>
public class AuthThrottlePolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
    private static readonly AuthThrottleOptions Options = new();

    private static AuthThrottleState? Run(Func<AuthThrottleState?, DateTimeOffset, AuthThrottleStep> admit, AuthThrottleState? state, DateTimeOffset at, out AuthThrottleStep step)
    {
        step = admit(state, at);
        return step.Admitted ? step.Next : state;
    }

    private static AuthThrottleStep Pair(AuthThrottleState? s, DateTimeOffset at) => AuthThrottlePolicy.AdmitPair(s, at, Options);

    private static AuthThrottleStep Identifier(AuthThrottleState? s, DateTimeOffset at) => AuthThrottlePolicy.AdmitIdentifier(s, at, Options);

    [Fact]
    public void Pair_FiveAdmittedThenBlockedFor30Seconds()
    {
        AuthThrottleState? state = null;
        for (var i = 0; i < 5; i++)
        {
            state = Run(Pair, state, T0.AddSeconds(i), out var step);
            Assert.True(step.Admitted);
        }

        Assert.Equal(5, state!.FailureCount);
        Assert.Equal(T0.AddSeconds(4 + 30), state.BlockedUntil);
        Run(Pair, state, T0.AddSeconds(5), out var rejected);
        Assert.False(rejected.Admitted);
        Assert.Equal(TimeSpan.FromSeconds(29), rejected.RetryAfter);
    }

    [Theory]
    [InlineData(5, 30)]
    [InlineData(6, 60)]
    [InlineData(7, 120)]
    [InlineData(9, 480)]
    [InlineData(10, 900)]
    [InlineData(30, 900)]
    [InlineData(int.MaxValue / 2, 900)]
    public void Pair_DelayDoublesAndIsCapped(int count, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), AuthThrottlePolicy.PairDelay(count, Options));
    }

    [Fact]
    public void Pair_BelowTheLimit_TheAnchoredWindowResets()
    {
        var state = new AuthThrottleState(T0, 4, null);
        Run(Pair, state, T0.AddMinutes(15).AddTicks(-10), out var inWindow);
        Assert.Equal(5, inWindow.Next!.FailureCount);
        Run(Pair, state, T0.AddMinutes(15), out var fresh);
        Assert.Equal(1, fresh.Next!.FailureCount);
        Assert.Equal(T0.AddMinutes(15), fresh.Next.WindowStart);
    }

    [Fact]
    public void Pair_EscalationEndsOnlyAfterMoreThanAFullWindowWithoutAnAttempt()
    {
        var capped = new AuthThrottleState(T0, 12, T0.AddMinutes(15));
        Run(Pair, capped, T0.AddMinutes(15), out var atTheBoundary);
        Assert.True(atTheBoundary.Admitted);
        Assert.Equal(13, atTheBoundary.Next!.FailureCount); // still escalated: 4 per hour

        Run(Pair, capped, T0.AddMinutes(15).AddTicks(10), out var after);
        Assert.Equal(1, after.Next!.FailureCount);
    }

    [Fact]
    public void Identifier_TenthAttemptEntersSlowMode_ThenOneSlotPer30Seconds()
    {
        AuthThrottleState? state = null;
        AuthThrottleStep step = default;
        for (var i = 0; i < 10; i++)
        {
            state = Run(Identifier, state, T0.AddSeconds(i), out step);
            Assert.True(step.Admitted);
        }

        Assert.True(step.EnteredSlowMode);
        Assert.Equal(T0.AddSeconds(9 + 30), state!.BlockedUntil);

        Run(Identifier, state, T0.AddSeconds(20), out var rejected);
        Assert.False(rejected.Admitted);
        Assert.Equal(TimeSpan.FromSeconds(19), rejected.RetryAfter);

        state = Run(Identifier, state, T0.AddSeconds(39), out var slot);
        Assert.True(slot.Admitted);
        Assert.False(slot.EnteredSlowMode);
        Assert.Equal(T0.AddSeconds(69), state!.BlockedUntil);
    }

    [Fact]
    public void Identifier_SlowModeEndsMoreThan15MinutesAfterTheLastAttempt()
    {
        var slow = new AuthThrottleState(T0, 10, T0.AddSeconds(30));
        Run(Identifier, slow, T0.AddMinutes(15), out var boundary);
        Assert.Equal(10, boundary.Next!.FailureCount);
        Run(Identifier, slow, T0.AddMinutes(15).AddTicks(10), out var fresh);
        Assert.Equal(1, fresh.Next!.FailureCount);
        Assert.Null(fresh.Next.BlockedUntil);
    }

    [Fact]
    public void Identifier_ASuccessReleasesItsCount_ButNotSlowMode()
    {
        var normal = new AuthThrottleState(T0, 4, null);
        var (changed, next) = AuthThrottlePolicy.ReleaseIdentifier(normal, T0.AddSeconds(5), false, Options);
        Assert.True(changed);
        Assert.Equal(3, next!.FailureCount);

        var single = new AuthThrottleState(T0, 1, null);
        Assert.Equal((true, (AuthThrottleState?)null), AuthThrottlePolicy.ReleaseIdentifier(single, T0, false, Options));

        var slow = new AuthThrottleState(T0.AddSeconds(40), 10, T0.AddSeconds(70));
        Assert.False(AuthThrottlePolicy.ReleaseIdentifier(slow, T0.AddSeconds(40), false, Options).Changed);
    }

    [Fact]
    public void Identifier_ASuccessThatItselfEnteredSlowMode_UndoesIt_IfNothingFollowed()
    {
        var entered = new AuthThrottleState(T0, 10, T0.AddSeconds(30));
        var (changed, next) = AuthThrottlePolicy.ReleaseIdentifier(entered, T0, true, Options);
        Assert.True(changed);
        Assert.Equal(9, next!.FailureCount);
        Assert.Null(next.BlockedUntil);

        var followed = new AuthThrottleState(T0.AddSeconds(31), 10, T0.AddSeconds(61));
        Assert.False(AuthThrottlePolicy.ReleaseIdentifier(followed, T0, true, Options).Changed);
    }

    [Fact]
    public void Identifier_ReleaseOfAnAttemptFromAnEarlierWindow_ChangesNothing()
    {
        var newer = new AuthThrottleState(T0.AddMinutes(20), 2, null);
        Assert.False(AuthThrottlePolicy.ReleaseIdentifier(newer, T0, false, Options).Changed);
    }

    [Fact]
    public void ChangePassword_FiveThen429UntilTheWindowPasses()
    {
        AuthThrottleState? state = null;
        for (var i = 0; i < 5; i++)
        {
            var step = AuthThrottlePolicy.AdmitChangePassword(state, T0.AddMinutes(i), Options);
            Assert.True(step.Admitted);
            state = step.Next;
        }

        var rejected = AuthThrottlePolicy.AdmitChangePassword(state, T0.AddMinutes(5), Options);
        Assert.False(rejected.Admitted);
        Assert.Equal(TimeSpan.FromMinutes(10), rejected.RetryAfter);
        Assert.True(AuthThrottlePolicy.AdmitChangePassword(state, T0.AddMinutes(15), Options).Admitted);

        var (changed, released) = AuthThrottlePolicy.ReleaseChangePassword(state, T0.AddMinutes(1));
        Assert.True(changed);
        Assert.Equal(4, released!.FailureCount);
    }

    [Fact]
    public void Options_InvalidValuesAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new AuthThrottleOptions { PairFailureLimit = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AuthThrottleOptions { IdentifierSlot = TimeSpan.Zero }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AuthThrottleOptions { LockTimeout = TimeSpan.FromMinutes(1) }.Validate());
        new AuthThrottleOptions().Validate();
    }
}
