using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Contractra.Infrastructure.Persistence.Throttling;

/// <summary>
/// Shared store for the persisted throttle layers (auth contract v1.5 §17;
/// ADR 0005 §3.4, §4). Generic and policy-free: the caller's decision function
/// is the policy (Slice 6, pending review - contract §22).
/// </summary>
public sealed class AuthThrottleStore
{
    /// <summary>Attempts of one step when a concurrent transaction keeps creating a row first.</summary>
    public const int MaxAttempts = 3;

    public const int MaxBatchSize = 10_000;

    public static readonly TimeSpan MaxLockTimeout = TimeSpan.FromSeconds(30);

    private const string SelectForUpdateSql =
        "SELECT window_start, failure_count, blocked_until FROM auth_throttle_counters WHERE scope = @scope AND key_hash = @key FOR UPDATE;";

    private const string SelectSql =
        "SELECT window_start, failure_count, blocked_until FROM auth_throttle_counters WHERE scope = @scope AND key_hash = @key;";

    private const string InsertIfAbsentSql =
        "INSERT INTO auth_throttle_counters (scope, key_hash, window_start, failure_count, blocked_until) " +
        "VALUES (@scope, @key, @window_start, @failure_count, @blocked_until) ON CONFLICT (scope, key_hash) DO NOTHING;";

    private const string UpdateSql =
        "UPDATE auth_throttle_counters SET window_start = @window_start, failure_count = @failure_count, blocked_until = @blocked_until " +
        "WHERE scope = @scope AND key_hash = @key;";

    private const string DeleteSql =
        "DELETE FROM auth_throttle_counters WHERE scope = @scope AND key_hash = @key;";

    private readonly ContractraDbContext _dbContext;

    public AuthThrottleStore(ContractraDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <summary>
    /// One throttle step in its own short transaction (never held while a
    /// password is hashed): locks the rows of <paramref name="keys"/> in
    /// (scope, key_hash) order, passes their current states (null = no row) to
    /// <paramref name="decide"/>, writes the decision's changes and commits.
    /// All changes are written or none. If a concurrent step created a missing
    /// row first, the whole step is retried with fresh states (up to
    /// <see cref="MaxAttempts"/>), so <paramref name="decide"/> must be pure. A
    /// decision that depends on a row being absent should write that row, so a
    /// concurrent creation is detected. Waiting for a lock longer than
    /// <paramref name="lockTimeout"/> fails with SQLSTATE 55P03 and changes
    /// nothing; any failure leaves the store unchanged, and Slice 6 fails closed.
    /// </summary>
    public async Task<TResult> ApplyAsync<TResult>(
        IReadOnlyCollection<AuthThrottleKey> keys,
        Func<IReadOnlyDictionary<AuthThrottleKey, AuthThrottleState?>, AuthThrottleDecision<TResult>> decide,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(decide);

        if (keys.Count == 0 || keys.Any(k => k is null))
        {
            throw new ArgumentException("A throttle step needs at least one key, and no null key.", nameof(keys));
        }

        if (lockTimeout <= TimeSpan.Zero || lockTimeout > MaxLockTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(lockTimeout), lockTimeout, $"The lock timeout must be positive and at most {MaxLockTimeout}.");
        }

        if (_dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "A throttle step runs in its own short transaction; it cannot join the caller's transaction.");
        }

        var ordered = keys.Distinct().Order().ToList();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
            var dbTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();

            await SetLockTimeoutAsync(connection, dbTransaction, lockTimeout, cancellationToken);

            var states = new Dictionary<AuthThrottleKey, AuthThrottleState?>();
            foreach (var key in ordered)
            {
                states[key] = await ReadAsync(connection, dbTransaction, SelectForUpdateSql, key, cancellationToken);
            }

            var decision = decide(states) ?? throw new InvalidOperationException("The throttle decision function returned null.");
            if (decision.Changes.Keys.FirstOrDefault(changed => !states.ContainsKey(changed)) is { } unknown)
            {
                throw new InvalidOperationException($"The decision changes a '{unknown.Scope}' row that was not one of the step's keys.");
            }

            if (await WriteAsync(connection, dbTransaction, ordered, states, decision.Changes, cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return decision.Result;
            }

            await transaction.RollbackAsync(cancellationToken);
        }

        throw new InvalidOperationException(
            $"The throttle step could not be applied after {MaxAttempts} attempts: its rows kept being created concurrently.");
    }

    /// <summary>The current state of one row, without locking it (null = no row).</summary>
    public async Task<AuthThrottleState?> GetAsync(AuthThrottleKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var database = _dbContext.Database;
        await database.OpenConnectionAsync(cancellationToken);
        try
        {
            return await ReadAsync(
                (NpgsqlConnection)database.GetDbConnection(),
                (NpgsqlTransaction?)database.CurrentTransaction?.GetDbTransaction(),
                SelectSql,
                key,
                cancellationToken);
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Deletes at most <paramref name="batchSize"/> expired rows: rows of a scope
    /// listed in <paramref name="windowByScope"/> whose window has passed
    /// (window_start + window &lt;= now) and that are not blocked at
    /// <paramref name="now"/>. Rows of unlisted scopes are never deleted. Rows
    /// locked by a running step are skipped (FOR UPDATE SKIP LOCKED), so cleanup
    /// never waits on an admission. Call repeatedly until it returns 0. Returns
    /// the number of rows deleted. This is the only deletion of auth data in
    /// Slice 2B (ADR 0005 E24).
    /// </summary>
    public async Task<int> DeleteExpiredAsync(
        DateTimeOffset now,
        IReadOnlyDictionary<string, TimeSpan> windowByScope,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(windowByScope);

        if (now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be in UTC (offset 00:00).", nameof(now));
        }

        if (batchSize < 1 || batchSize > MaxBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, $"The batch size must be between 1 and {MaxBatchSize}.");
        }

        var conditions = new List<string>();
        var parameters = new List<NpgsqlParameter>
        {
            Timestamp("now", now),
            new("batch", NpgsqlDbType.Integer) { Value = batchSize },
        };

        foreach (var (scope, window) in windowByScope)
        {
            if (!AuthThrottleScopes.All.Contains(scope, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Unknown throttle scope '{scope}'.", nameof(windowByScope));
            }

            if (window <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(windowByScope), window, $"The window of '{scope}' must be positive.");
            }

            var index = conditions.Count;
            conditions.Add($"(scope = @scope{index} AND window_start <= @cutoff{index})");
            parameters.Add(new NpgsqlParameter($"scope{index}", NpgsqlDbType.Text) { Value = scope });
            parameters.Add(Timestamp($"cutoff{index}", now - window));
        }

        if (conditions.Count == 0)
        {
            return 0;
        }

        var sql =
            "DELETE FROM auth_throttle_counters AS c USING (" +
            "SELECT scope, key_hash FROM auth_throttle_counters " +
            $"WHERE ({string.Join(" OR ", conditions)}) AND (blocked_until IS NULL OR blocked_until <= @now) " +
            "LIMIT @batch FOR UPDATE SKIP LOCKED) AS expired " +
            "WHERE c.scope = expired.scope AND c.key_hash = expired.key_hash;";

        return await _dbContext.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
    }

    /// <returns>False when a missing row was created by a concurrent step first (retry the step).</returns>
    private static async Task<bool> WriteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IEnumerable<AuthThrottleKey> ordered,
        IReadOnlyDictionary<AuthThrottleKey, AuthThrottleState?> states,
        IReadOnlyDictionary<AuthThrottleKey, AuthThrottleState?> changes,
        CancellationToken cancellationToken)
    {
        foreach (var key in ordered)
        {
            if (!changes.TryGetValue(key, out var next))
            {
                continue;
            }

            var existed = states[key] is not null;
            if (next is null)
            {
                if (existed)
                {
                    await ExecuteAsync(connection, transaction, DeleteSql, key, null, cancellationToken);
                }

                continue;
            }

            if (existed)
            {
                await ExecuteAsync(connection, transaction, UpdateSql, key, next, cancellationToken);
            }
            else if (await ExecuteAsync(connection, transaction, InsertIfAbsentSql, key, next, cancellationToken) == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task SetLockTimeoutAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, TimeSpan lockTimeout, CancellationToken cancellationToken)
    {
        // set_config(..., is_local => true) = SET LOCAL: ends with the transaction.
        await using var command = new NpgsqlCommand("SELECT set_config('lock_timeout', @value, true);", connection, transaction);
        command.Parameters.AddWithValue("value", $"{(int)Math.Ceiling(lockTimeout.TotalMilliseconds)}ms");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<AuthThrottleState?> ReadAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, AuthThrottleKey key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddKey(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AuthThrottleState(
            reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
    }

    private static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        AuthThrottleKey key,
        AuthThrottleState? state,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddKey(command, key);
        if (state is not null)
        {
            command.Parameters.Add(Timestamp("window_start", state.WindowStart));
            command.Parameters.Add(new NpgsqlParameter("failure_count", NpgsqlDbType.Integer) { Value = state.FailureCount });
            command.Parameters.Add(Timestamp("blocked_until", state.BlockedUntil));
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddKey(NpgsqlCommand command, AuthThrottleKey key)
    {
        command.Parameters.Add(new NpgsqlParameter("scope", NpgsqlDbType.Text) { Value = key.Scope });
        command.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Bytea) { Value = key.KeyHashBytes });
    }

    private static NpgsqlParameter Timestamp(string name, DateTimeOffset? value) =>
        new(name, NpgsqlDbType.TimestampTz) { Value = value is { } v ? v : DBNull.Value };
}
