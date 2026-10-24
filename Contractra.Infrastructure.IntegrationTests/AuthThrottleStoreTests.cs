using System.Security.Cryptography;
using Contractra.Infrastructure.Persistence.Throttling;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// The shared throttle store (US-BE-008; auth contract v1.5 §17; ADR 0005
/// §3.4, §4). Policy-free: these tests use a trivial "count one more attempt"
/// decision, which is not the Slice 6 policy.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public class AuthThrottleStoreTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly PostgresTestDatabaseFixture _fixture;

    public AuthThrottleStoreTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Apply_CreatesUpdatesAndDeletesRows_AsTheDecisionSays()
    {
        var key = NewKey(AuthThrottleScopes.LoginPair);

        Assert.Equal(1, await IncrementAsync(key));
        Assert.Equal(2, await IncrementAsync(key));
        Assert.Equal(new AuthThrottleState(T0, 2, null), await GetAsync(key));

        var blocked = new AuthThrottleState(T0, 2, T0.AddSeconds(30));
        await ApplyAsync(new[] { key }, _ => Decide(key, blocked));
        Assert.Equal(blocked, await GetAsync(key));

        await ApplyAsync(new[] { key }, _ => Decide(key, null));
        Assert.Null(await GetAsync(key));
    }

    [Fact]
    public async Task Apply_GivesTheDecisionTheCurrentStateOfEveryKey_NullWhenMissing()
    {
        var present = NewKey(AuthThrottleScopes.Identifier);
        var missing = NewKey(AuthThrottleScopes.Identifier);
        await IncrementAsync(present);

        IReadOnlyDictionary<AuthThrottleKey, AuthThrottleState?>? seen = null;
        var result = await ApplyAsync(new[] { present, missing }, states =>
        {
            seen = states;
            return new AuthThrottleDecision<string>("no change");
        });

        Assert.Equal("no change", result);
        Assert.Equal(2, seen!.Count);
        Assert.Equal(1, seen[present]!.FailureCount);
        Assert.Null(seen[missing]);
        Assert.Null(await GetAsync(missing));
    }

    [Fact]
    public async Task Apply_WritesAllChangesOrNone()
    {
        var first = NewKey(AuthThrottleScopes.LoginPair);
        var second = NewKey(AuthThrottleScopes.Identifier);
        await IncrementAsync(first);

        // The second write violates a CHECK through raw state crafted below the
        // record's own validation; the store must roll the first one back.
        await using (var connection = await OpenAsync())
        {
            await PostgresAssert.ExecuteAsync(connection, null,
                "ALTER TABLE auth_throttle_counters ADD CONSTRAINT ck_test_reject_far_future CHECK (window_start < '2100-01-01');");
        }

        try
        {
            var farFuture = new DateTimeOffset(2200, 1, 1, 0, 0, 0, TimeSpan.Zero);
            await Assert.ThrowsAsync<PostgresException>(() => ApplyAsync(new[] { first, second }, _ =>
                new AuthThrottleDecision<int>(0, new Dictionary<AuthThrottleKey, AuthThrottleState?>
                {
                    [first] = new(T0, 5, null),
                    [second] = new(farFuture, 1, null),
                })));
        }
        finally
        {
            await using var connection = await OpenAsync();
            await PostgresAssert.ExecuteAsync(connection, null, "ALTER TABLE auth_throttle_counters DROP CONSTRAINT ck_test_reject_far_future;");
        }

        Assert.Equal(1, (await GetAsync(first))!.FailureCount);
        Assert.Null(await GetAsync(second));
    }

    [Fact]
    public async Task Apply_RejectsAChangeForAKeyItWasNotGiven()
    {
        var given = NewKey(AuthThrottleScopes.LoginPair);
        var other = NewKey(AuthThrottleScopes.LoginPair);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ApplyAsync(new[] { given }, _ => Decide(other, new AuthThrottleState(T0, 1, null))));
        Assert.Null(await GetAsync(other));
    }

    [Fact]
    public async Task Apply_RejectsInvalidArguments_AndAnAmbientTransaction()
    {
        await using var context = _fixture.CreateDbContext();
        var store = new AuthThrottleStore(context);
        var key = NewKey(AuthThrottleScopes.LoginPair);

        await Assert.ThrowsAsync<ArgumentException>(() => store.ApplyAsync(Array.Empty<AuthThrottleKey>(), _ => Decide(key, null), LockTimeout));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ApplyAsync(new[] { key }, _ => Decide(key, null), TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ApplyAsync(new[] { key }, _ => Decide(key, null), AuthThrottleStore.MaxLockTimeout + TimeSpan.FromSeconds(1)));

        await using (await context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAsync(new[] { key }, _ => Decide(key, null), LockTimeout));
        }
    }

    [Fact]
    public async Task ConcurrentStepsOnOneKey_LoseNoUpdate()
    {
        var key = NewKey(AuthThrottleScopes.Identifier);
        const int Steps = 50;

        var errors = await Concurrently.RunAsync(Steps, _ => IncrementAsync(key));

        Assert.All(errors, e => Assert.Null(e));
        Assert.Equal(Steps, (await GetAsync(key))!.FailureCount);
    }

    [Fact]
    public async Task StepsOnTheSameKeysInOppositeOrder_DoNotDeadlock()
    {
        var low = NewKey(AuthThrottleScopes.Identifier, 0x01);
        var high = NewKey(AuthThrottleScopes.LoginPair, 0xF0);
        const int Steps = 40;

        var errors = await Concurrently.RunAsync(Steps, i =>
            ApplyAsync(i % 2 == 0 ? new[] { low, high } : new[] { high, low }, states =>
                new AuthThrottleDecision<int>(0, new Dictionary<AuthThrottleKey, AuthThrottleState?>
                {
                    [low] = Next(states[low]),
                    [high] = Next(states[high]),
                })));

        Assert.All(errors, e => Assert.Null(e));
        Assert.Equal(Steps, (await GetAsync(low))!.FailureCount);
        Assert.Equal(Steps, (await GetAsync(high))!.FailureCount);
    }

    [Fact]
    public async Task Apply_GivesUpWithALockError_WhenARowStaysLockedPastTheTimeout_AndChangesNothing()
    {
        var key = NewKey(AuthThrottleScopes.ChangePasswordUser);
        await IncrementAsync(key);

        await using var holder = await OpenAsync();
        await using var holding = await holder.BeginTransactionAsync();
        await LockRowAsync(holder, holding, key);

        await using var context = _fixture.CreateDbContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            new AuthThrottleStore(context).ApplyAsync(new[] { key }, s => Decide(key, Next(s[key])), TimeSpan.FromMilliseconds(200)));
        Assert.Equal("55P03", error.SqlState);

        await holding.RollbackAsync();
        Assert.Equal(1, (await GetAsync(key))!.FailureCount);
    }

    [Fact]
    public async Task DeleteExpired_RemovesOnlyExpiredUnblockedRows_InBoundedBatches_SkippingLockedRows()
    {
        // A year before every other test's rows (window_start >= T0), so this
        // test's cutoffs cannot reach rows it did not create in this shared database.
        var now = T0.AddYears(-1);
        var expired = Enumerable.Range(0, 5).Select(_ => NewKey(AuthThrottleScopes.LoginPair)).ToList();
        var lockedExpired = NewKey(AuthThrottleScopes.LoginPair);
        var stillBlocked = NewKey(AuthThrottleScopes.Identifier);
        var live = NewKey(AuthThrottleScopes.Identifier);
        var noWindowConfigured = NewKey(AuthThrottleScopes.ChangePasswordUser);

        foreach (var key in expired.Append(lockedExpired))
        {
            await SetAsync(key, new AuthThrottleState(now - Window, 3, null));
        }

        await SetAsync(stillBlocked, new AuthThrottleState(now - Window - Window, 12, now.AddSeconds(30)));
        await SetAsync(live, new AuthThrottleState(now - Window + TimeSpan.FromSeconds(1), 2, null));
        await SetAsync(noWindowConfigured, new AuthThrottleState(now.AddDays(-30), 5, null));

        var windows = new Dictionary<string, TimeSpan>
        {
            [AuthThrottleScopes.LoginPair] = Window,
            [AuthThrottleScopes.Identifier] = Window,
        };

        await using var holder = await OpenAsync();
        await using var holding = await holder.BeginTransactionAsync();
        await LockRowAsync(holder, holding, lockedExpired);

        await using var context = _fixture.CreateDbContext();
        var store = new AuthThrottleStore(context);
        var batches = new List<int>();
        int deleted;
        do
        {
            deleted = await store.DeleteExpiredAsync(now, windows, batchSize: 2);
            batches.Add(deleted);
        }
        while (deleted > 0);

        Assert.Equal(new[] { 2, 2, 1, 0 }, batches);
        foreach (var key in expired)
        {
            Assert.Null(await GetAsync(key));
        }

        Assert.NotNull(await GetAsync(lockedExpired));
        Assert.NotNull(await GetAsync(stillBlocked));
        Assert.NotNull(await GetAsync(live));
        Assert.NotNull(await GetAsync(noWindowConfigured));

        await holding.RollbackAsync();
        Assert.Equal(1, await store.DeleteExpiredAsync(now, windows, batchSize: 2));
        Assert.Null(await GetAsync(lockedExpired));
    }

    [Fact]
    public async Task DeleteExpired_RejectsInvalidArguments()
    {
        await using var context = _fixture.CreateDbContext();
        var store = new AuthThrottleStore(context);
        var windows = new Dictionary<string, TimeSpan> { [AuthThrottleScopes.LoginPair] = Window };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.DeleteExpiredAsync(T0, windows, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteExpiredAsync(T0.ToOffset(TimeSpan.FromHours(2)), windows, 10));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteExpiredAsync(T0, new Dictionary<string, TimeSpan> { ["login"] = Window }, 10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.DeleteExpiredAsync(T0, new Dictionary<string, TimeSpan> { [AuthThrottleScopes.LoginPair] = TimeSpan.Zero }, 10));
        Assert.Equal(0, await store.DeleteExpiredAsync(T0, new Dictionary<string, TimeSpan>(), 10));
    }

    [Fact]
    public void Key_ValidatesComparesByValue_AndOrdersByScopeThenHash()
    {
        var hash = RandomNumberGenerator.GetBytes(32);
        var a = new AuthThrottleKey(AuthThrottleScopes.Identifier, hash);
        var b = new AuthThrottleKey(AuthThrottleScopes.Identifier, (byte[])hash.Clone());
        hash[0] ^= 0xFF;

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(hash, a.KeyHash.ToArray());
        Assert.True(new AuthThrottleKey(AuthThrottleScopes.ChangePasswordUser, new byte[32]).CompareTo(a) < 0);
        Assert.Throws<ArgumentException>(() => new AuthThrottleKey("login", new byte[32]));
        Assert.Throws<ArgumentException>(() => new AuthThrottleKey(AuthThrottleScopes.Identifier, new byte[31]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthThrottleState(T0, 0, null));
        Assert.Throws<ArgumentException>(() => new AuthThrottleState(T0, 1, T0.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => new AuthThrottleState(T0.ToOffset(TimeSpan.FromHours(2)), 1, null));
    }

    private Task<int> IncrementAsync(AuthThrottleKey key) =>
        ApplyAsync(new[] { key }, states =>
        {
            var next = Next(states[key]);
            return new AuthThrottleDecision<int>(next.FailureCount, new Dictionary<AuthThrottleKey, AuthThrottleState?> { [key] = next });
        });

    private static AuthThrottleState Next(AuthThrottleState? current) =>
        current is null ? new AuthThrottleState(T0, 1, null) : new AuthThrottleState(current.WindowStart, current.FailureCount + 1, current.BlockedUntil);

    private static AuthThrottleDecision<int> Decide(AuthThrottleKey key, AuthThrottleState? state) =>
        new(0, new Dictionary<AuthThrottleKey, AuthThrottleState?> { [key] = state });

    private Task SetAsync(AuthThrottleKey key, AuthThrottleState state) =>
        ApplyAsync(new[] { key }, _ => Decide(key, state));

    private async Task<TResult> ApplyAsync<TResult>(
        IReadOnlyCollection<AuthThrottleKey> keys,
        Func<IReadOnlyDictionary<AuthThrottleKey, AuthThrottleState?>, AuthThrottleDecision<TResult>> decide)
    {
        await using var context = _fixture.CreateDbContext();
        return await new AuthThrottleStore(context).ApplyAsync(keys, decide, LockTimeout);
    }

    private async Task<AuthThrottleState?> GetAsync(AuthThrottleKey key)
    {
        await using var context = _fixture.CreateDbContext();
        return await new AuthThrottleStore(context).GetAsync(key);
    }

    private static async Task LockRowAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AuthThrottleKey key)
    {
        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM auth_throttle_counters WHERE scope = @scope AND key_hash = @key FOR UPDATE;", connection, transaction);
        command.Parameters.AddWithValue("scope", key.Scope);
        command.Parameters.AddWithValue("key", key.KeyHash.ToArray());
        Assert.Equal(1, await command.ExecuteScalarAsync());
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static AuthThrottleKey NewKey(string scope, byte? first = null)
    {
        var hash = RandomNumberGenerator.GetBytes(32);
        if (first is { } value)
        {
            hash[0] = value;
        }

        return new AuthThrottleKey(scope, hash);
    }
}
