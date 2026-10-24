using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit.Abstractions;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// TASK-BE-034: the EF Core / Npgsql / PostgreSQL behaviours that ADR 0005 §5
/// lists as not documented, checked on the test server before the Slice 2B
/// schema relies on them. They use their own spike_* tables in this class's
/// ephemeral database, so they keep guarding the behaviour after provider
/// upgrades.
/// </summary>
[Collection(ConcurrencyTestCollection.Name)]
public class PersistenceBehaviorSpikeTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string Lock = "spike_lock";

    private readonly PostgresTestDatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PersistenceBehaviorSpikeTests(PostgresTestDatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    // ---------------------------------------------------------------- spike 1

    /// <summary>
    /// Observed (Npgsql EF Core 10.0.3): the migration operation does carry an
    /// "xmin" column; the Npgsql SQL generator leaves it out of the DDL, so the
    /// table gets no such column (PostgreSQL would reject one: xmin is a system
    /// column). The migration file will therefore list xmin, and that is safe.
    /// </summary>
    [Fact]
    public async Task Spike1_UintShadowRowVersion_MapsToXmin_AndTheGeneratedDdlCreatesNoColumnForIt()
    {
        await using var context = CreateSpikeContext();

        var property = context.Model.FindEntityType(typeof(SpikeSession))!.FindProperty(SpikeContext.RowVersion)!;
        Assert.True(property.IsShadowProperty());
        Assert.True(property.IsConcurrencyToken);
        Assert.Equal("xmin", property.GetColumnName());

        var createTable = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, context.GetService<IDesignTimeModel>().Model.GetRelationalModel())
            .OfType<CreateTableOperation>()
            .Single(o => o.Name == "spike_sessions");
        _output.WriteLine($"CreateTableOperation columns: {string.Join(", ", createTable.Columns.Select(c => $"{c.Name}:{c.ColumnType}"))}");

        // Generate the DDL for the same operation under another table name and run it.
        const string DdlTable = "spike_sessions_ddl";
        createTable.Name = DdlTable;
        createTable.PrimaryKey!.Table = DdlTable;
        createTable.PrimaryKey.Name = "pk_spike_sessions_ddl";
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(new MigrationOperation[] { createTable }, context.Model);
        var ddl = string.Join(Environment.NewLine, commands.Select(c => c.CommandText));
        _output.WriteLine(ddl);
        Assert.DoesNotContain("xmin", ddl, StringComparison.OrdinalIgnoreCase);

        await using var connection = await OpenAsync();
        await PostgresAssert.ExecuteAsync(connection, null, $"DROP TABLE IF EXISTS {DdlTable};");
        await PostgresAssert.ExecuteAsync(connection, null, ddl);
        Assert.Equal(2, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = @table AND column_name IN ('id', 'note');", ("table", DdlTable)));
        Assert.Equal(2, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = @table;", ("table", DdlTable)));
    }

    [Fact]
    public async Task Spike1_XminRowVersion_RejectsAStaleUpdate_IncludingAfterABulkUpdate()
    {
        await EnsureSpikeTablesAsync();
        var id = Guid.CreateVersion7();
        await using (var seed = CreateSpikeContext())
        {
            seed.Sessions.Add(new SpikeSession { Id = id, Note = "start" });
            await seed.SaveChangesAsync();
        }

        await using var first = CreateSpikeContext();
        await using var second = CreateSpikeContext();
        var firstCopy = await first.Sessions.SingleAsync(s => s.Id == id);
        var secondCopy = await second.Sessions.SingleAsync(s => s.Id == id);

        firstCopy.Note = "first";
        await first.SaveChangesAsync();

        secondCopy.Note = "second";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        // A set-based update (as a bulk revocation does) also changes xmin.
        await using var third = CreateSpikeContext();
        var thirdCopy = await third.Sessions.SingleAsync(s => s.Id == id);
        await using (var bulk = CreateSpikeContext())
        {
            Assert.Equal(1, await bulk.Sessions.Where(s => s.Id == id).ExecuteUpdateAsync(u => u.SetProperty(s => s.Note, "bulk")));
        }

        thirdCopy.Note = "third";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => third.SaveChangesAsync());

        // The winner reads the new xmin back and can save again.
        await using var fourth = CreateSpikeContext();
        var fourthCopy = await fourth.Sessions.SingleAsync(s => s.Id == id);
        fourthCopy.Note = "fourth";
        await fourth.SaveChangesAsync();
        fourthCopy.Note = "fifth";
        await fourth.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- spike 2

    [Fact]
    public async Task Spike2_DeferredExclusion_IgnoresStatementOrder_AndRejectsAtCommit()
    {
        await EnsureSpikeTablesAsync();
        var session = Guid.CreateVersion7();
        var oldToken = Guid.CreateVersion7();

        await using var connection = await OpenAsync();
        await InsertOpenTokenAsync(connection, null, oldToken, session);

        // New open token first, old one consumed second: valid at commit.
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await InsertOpenTokenAsync(connection, transaction, Guid.CreateVersion7(), session);
            await PostgresAssert.ExecuteAsync(connection, transaction,
                "UPDATE spike_tokens SET consumed_at = now() WHERE id = @id;", ("id", oldToken));
            await transaction.CommitAsync();
        }

        // Two open tokens at commit: rejected then, not at the INSERT.
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await InsertOpenTokenAsync(connection, transaction, Guid.CreateVersion7(), session);
            var error = await PostgresAssert.FailsWithAsync(() => transaction.CommitAsync(), "23P01", "ex_spike_tokens_one_open_per_session");
            _output.WriteLine($"Two open rows at commit: {error.SqlState} {error.ConstraintName}");
        }

        Assert.Equal(1, await OpenTokenCountAsync(connection, session));
    }

    [Fact]
    public async Task Spike2_DeferredExclusion_ConcurrentCommitsOfConflictingRows_ExactlyOneCommits()
    {
        await EnsureSpikeTablesAsync();
        var losers = new List<string>();

        for (var round = 0; round < 5; round++)
        {
            var session = Guid.CreateVersion7();
            await using var first = await OpenAsync();
            await using var second = await OpenAsync();
            await using var firstTransaction = await first.BeginTransactionAsync();
            await using var secondTransaction = await second.BeginTransactionAsync();

            // Neither INSERT waits: the deferred check runs at commit.
            await InsertOpenTokenAsync(first, firstTransaction, Guid.CreateVersion7(), session);
            await InsertOpenTokenAsync(second, secondTransaction, Guid.CreateVersion7(), session);

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var results = await Task.WhenAll(
                CommitAfterAsync(gate.Task, firstTransaction),
                CommitAfterAsync(gate.Task, secondTransaction),
                Release(gate));

            var outcomes = results.Take(2).ToArray();
            Assert.Equal(1, outcomes.Count(o => o is null));
            var loser = outcomes.Single(o => o is not null)!;
            Assert.Contains(loser, new[] { "23P01", "40P01" });
            losers.Add(loser);

            await using var check = await OpenAsync();
            Assert.Equal(1, await OpenTokenCountAsync(check, session));
        }

        _output.WriteLine($"Concurrent-commit losers' SQLSTATEs: {string.Join(", ", losers)}");
    }

    // ---------------------------------------------------------------- spike 3

    [Fact]
    public async Task Spike3_NullableConcurrencyToken_UsesIsNull_AndTheConcurrentLoserChangesNothing()
    {
        await EnsureSpikeTablesAsync();
        var session = Guid.CreateVersion7();
        var token = Guid.CreateVersion7();
        await using (var seed = CreateSpikeContext())
        {
            seed.Tokens.Add(new SpikeToken { Id = token, SessionId = session });
            await seed.SaveChangesAsync();
        }

        var commands = new List<string>();
        await using var first = CreateSpikeContext(commands);
        await using var second = CreateSpikeContext();
        var firstCopy = await first.Tokens.SingleAsync(t => t.Id == token);
        var secondCopy = await second.Tokens.SingleAsync(t => t.Id == token);

        var firstTime = new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
        firstCopy.ConsumedAt = firstTime;
        await first.SaveChangesAsync();

        var update = commands.Single(c => c.Contains("UPDATE", StringComparison.Ordinal));
        _output.WriteLine(update);
        Assert.Contains("AND consumed_at IS NULL", update, StringComparison.Ordinal);

        secondCopy.ConsumedAt = firstTime.AddSeconds(1);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var check = CreateSpikeContext();
        Assert.Equal(firstTime, (await check.Tokens.SingleAsync(t => t.Id == token)).ConsumedAt);
    }

    [Fact]
    public async Task Spike3_DeferredViolation_SurfacesFromSaveChanges_AndPersistsNothing()
    {
        await EnsureSpikeTablesAsync();
        var session = Guid.CreateVersion7();

        await using var context = CreateSpikeContext();
        context.Tokens.Add(new SpikeToken { Id = Guid.CreateVersion7(), SessionId = session });
        context.Tokens.Add(new SpikeToken { Id = Guid.CreateVersion7(), SessionId = session });

        var exception = await Record.ExceptionAsync(() => context.SaveChangesAsync());
        Assert.NotNull(exception);
        _output.WriteLine($"SaveChanges without a user transaction: {Describe(exception)}");
        Assert.Equal("23P01", FindPostgres(exception)?.SqlState);

        await using var connection = await OpenAsync();
        Assert.Equal(0, await OpenTokenCountAsync(connection, session));
    }

    [Fact]
    public async Task Spike3_InsideAUserTransaction_ADeferredViolationSurfacesAtCommit()
    {
        await EnsureSpikeTablesAsync();
        var session = Guid.CreateVersion7();

        await using var context = CreateSpikeContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Tokens.Add(new SpikeToken { Id = Guid.CreateVersion7(), SessionId = session });
        context.Tokens.Add(new SpikeToken { Id = Guid.CreateVersion7(), SessionId = session });
        await context.SaveChangesAsync();

        var exception = await Record.ExceptionAsync(() => transaction.CommitAsync());
        Assert.NotNull(exception);
        _output.WriteLine($"Commit of a user transaction: {Describe(exception)}");
        Assert.Equal("23P01", FindPostgres(exception)?.SqlState);

        await using var connection = await OpenAsync();
        Assert.Equal(0, await OpenTokenCountAsync(connection, session));
    }

    [Fact]
    public async Task Spike3_AFailedSaveInsideAUserTransaction_RollsBackOnlyToASavepoint()
    {
        await EnsureSpikeTablesAsync();

        // Hazard (E25): committing after a failed save persists the earlier work.
        var (earlier, conflicting) = await SeedConflictAsync();
        await using (var context = CreateSpikeContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await SaveEarlierWorkThenFailAsync(context, earlier, conflicting);
            await transaction.CommitAsync();
        }

        Assert.True(await SessionExistsAsync(earlier));

        // The E25 rule: never commit after a failed save; disposing rolls everything back.
        (earlier, conflicting) = await SeedConflictAsync();
        await using (var context = CreateSpikeContext())
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await SaveEarlierWorkThenFailAsync(context, earlier, conflicting);
        }

        Assert.False(await SessionExistsAsync(earlier));
    }

    // ---------------------------------------------------------------- spike 4

    [Fact]
    public async Task Spike4_LockOrCreate_ConcurrentStepsOnOneKey_LoseNoUpdate()
    {
        await EnsureSpikeTablesAsync();
        var key = RandomKey(0x10);
        const int Steps = 50;

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var steps = Enumerable.Range(0, Steps).Select(_ => IncrementAfterAsync(gate.Task, new[] { key })).ToList();
        gate.SetResult();
        var retries = await Task.WhenAll(steps);

        await using var connection = await OpenAsync();
        Assert.Equal(Steps, await ThrottleCountAsync(connection, key));
        _output.WriteLine($"Lock-or-create retries after a concurrent insert: {retries.Sum()}");
    }

    [Fact]
    public async Task Spike4_FixedLockOrder_OppositeInputOrder_DoesNotDeadlock()
    {
        await EnsureSpikeTablesAsync();
        var low = RandomKey(0x01);
        var high = RandomKey(0xF0);
        const int StepsPerOrder = 25;

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var steps = Enumerable.Range(0, StepsPerOrder)
            .SelectMany(_ => new[]
            {
                IncrementAfterAsync(gate.Task, new[] { low, high }),
                IncrementAfterAsync(gate.Task, new[] { high, low }),
            })
            .ToList();
        gate.SetResult();
        await Task.WhenAll(steps);

        await using var connection = await OpenAsync();
        Assert.Equal(2 * StepsPerOrder, await ThrottleCountAsync(connection, low));
        Assert.Equal(2 * StepsPerOrder, await ThrottleCountAsync(connection, high));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Prototype of the ADR 0005 §4 throttle step: lock the rows in
    /// (scope, key_hash) order, create a missing row with ON CONFLICT DO NOTHING
    /// (retry the whole step if another transaction created it first), write,
    /// commit. Returns how many retries were needed.
    /// </summary>
    private async Task<int> IncrementAfterAsync(Task gate, IReadOnlyList<byte[]> keys)
    {
        await gate;
        var ordered = keys.OrderBy(k => k, ByteArrayComparer.Instance).ToList();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var connection = await OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await PostgresAssert.ExecuteAsync(connection, transaction, "SET LOCAL lock_timeout = '5s';");

            var created = true;
            foreach (var key in ordered)
            {
                await using var select = new NpgsqlCommand(
                    "SELECT failure_count FROM spike_throttle WHERE scope = @scope AND key_hash = @key FOR UPDATE;",
                    connection, transaction);
                select.Parameters.AddWithValue("scope", Lock);
                select.Parameters.AddWithValue("key", key);
                var current = await select.ExecuteScalarAsync();

                if (current is int count)
                {
                    await PostgresAssert.ExecuteAsync(connection, transaction,
                        "UPDATE spike_throttle SET failure_count = @count WHERE scope = @scope AND key_hash = @key;",
                        ("count", count + 1), ("scope", Lock), ("key", key));
                    continue;
                }

                var inserted = await PostgresAssert.ExecuteAsync(connection, transaction,
                    "INSERT INTO spike_throttle (scope, key_hash, window_start, failure_count) VALUES (@scope, @key, now(), 1) " +
                    "ON CONFLICT (scope, key_hash) DO NOTHING;",
                    ("scope", Lock), ("key", key));
                if (inserted == 0)
                {
                    created = false;
                    break;
                }
            }

            if (!created)
            {
                await transaction.RollbackAsync();
                continue;
            }

            await transaction.CommitAsync();
            return attempt;
        }

        throw new InvalidOperationException("The throttle step could not be applied after 3 attempts.");
    }

    private async Task EnsureSpikeTablesAsync()
    {
        await using var connection = await OpenAsync();
        await PostgresAssert.ExecuteAsync(connection, null, """
            CREATE TABLE IF NOT EXISTS spike_sessions (
                id uuid PRIMARY KEY,
                note text NOT NULL);
            CREATE TABLE IF NOT EXISTS spike_tokens (
                id uuid PRIMARY KEY,
                session_id uuid NOT NULL,
                consumed_at timestamp with time zone NULL,
                CONSTRAINT ex_spike_tokens_one_open_per_session
                    EXCLUDE USING btree (session_id WITH =) WHERE (consumed_at IS NULL)
                    DEFERRABLE INITIALLY DEFERRED);
            CREATE TABLE IF NOT EXISTS spike_throttle (
                scope text NOT NULL,
                key_hash bytea NOT NULL,
                window_start timestamp with time zone NOT NULL,
                failure_count integer NOT NULL CHECK (failure_count >= 1),
                blocked_until timestamp with time zone NULL,
                PRIMARY KEY (scope, key_hash));
            """);
    }

    private async Task<(Guid Earlier, Guid Conflicting)> SeedConflictAsync()
    {
        var conflicting = Guid.CreateVersion7();
        await using var seed = CreateSpikeContext();
        seed.Sessions.Add(new SpikeSession { Id = conflicting, Note = "seed" });
        await seed.SaveChangesAsync();
        return (Guid.CreateVersion7(), conflicting);
    }

    private async Task SaveEarlierWorkThenFailAsync(SpikeContext context, Guid earlier, Guid conflicting)
    {
        context.Sessions.Add(new SpikeSession { Id = earlier, Note = "earlier work" });
        await context.SaveChangesAsync();

        var stale = await context.Sessions.SingleAsync(s => s.Id == conflicting);
        await using (var other = CreateSpikeContext())
        {
            await other.Sessions.Where(s => s.Id == conflicting).ExecuteUpdateAsync(u => u.SetProperty(s => s.Note, "changed elsewhere"));
        }

        stale.Note = "stale write";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
    }

    private async Task<bool> SessionExistsAsync(Guid id)
    {
        await using var connection = await OpenAsync();
        return await PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM spike_sessions WHERE id = @id;", ("id", id)) == 1;
    }

    private static Task<int> InsertOpenTokenAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid id, Guid session) =>
        PostgresAssert.ExecuteAsync(connection, transaction,
            "INSERT INTO spike_tokens (id, session_id) VALUES (@id, @session);", ("id", id), ("session", session));

    private static Task<long> OpenTokenCountAsync(NpgsqlConnection connection, Guid session) =>
        PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM spike_tokens WHERE session_id = @session AND consumed_at IS NULL;", ("session", session));

    private static async Task<long> ThrottleCountAsync(NpgsqlConnection connection, byte[] key)
    {
        await using var command = new NpgsqlCommand(
            "SELECT failure_count FROM spike_throttle WHERE scope = @scope AND key_hash = @key;", connection);
        command.Parameters.AddWithValue("scope", Lock);
        command.Parameters.AddWithValue("key", key);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string?> CommitAfterAsync(Task gate, NpgsqlTransaction transaction)
    {
        await gate;
        try
        {
            await transaction.CommitAsync();
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
    }

    private static Task<string?> Release(TaskCompletionSource gate)
    {
        gate.SetResult();
        return Task.FromResult<string?>(null);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private SpikeContext CreateSpikeContext(List<string>? commands = null)
    {
        var builder = new DbContextOptionsBuilder<SpikeContext>().UseNpgsql(_fixture.ConnectionString);
        if (commands is not null)
        {
            builder.LogTo(commands.Add, new[] { RelationalEventId.CommandExecuting });
        }

        return new SpikeContext(builder.Options);
    }

    private static byte[] RandomKey(byte first)
    {
        var key = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();
        key[0] = first;
        return key;
    }

    private static PostgresException? FindPostgres(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    private static string Describe(Exception exception)
    {
        var chain = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            chain.Add(current is PostgresException pg ? $"{current.GetType().Name}({pg.SqlState})" : current.GetType().Name);
        }

        return string.Join(" -> ", chain);
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public int Compare(byte[]? x, byte[]? y) => ((ReadOnlySpan<byte>)x).SequenceCompareTo(y);
    }

    private sealed class SpikeSession
    {
        public Guid Id { get; set; }

        public string Note { get; set; } = string.Empty;
    }

    private sealed class SpikeToken
    {
        public Guid Id { get; set; }

        public Guid SessionId { get; set; }

        public DateTimeOffset? ConsumedAt { get; set; }
    }

    private sealed class SpikeContext : DbContext
    {
        public const string RowVersion = "RowVersion";

        public SpikeContext(DbContextOptions<SpikeContext> options)
            : base(options)
        {
        }

        public DbSet<SpikeSession> Sessions => Set<SpikeSession>();

        public DbSet<SpikeToken> Tokens => Set<SpikeToken>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SpikeSession>(builder =>
            {
                builder.ToTable("spike_sessions");
                builder.HasKey(s => s.Id);
                builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
                builder.Property(s => s.Note).HasColumnName("note");
                builder.Property<uint>(RowVersion).IsRowVersion();
            });

            modelBuilder.Entity<SpikeToken>(builder =>
            {
                builder.ToTable("spike_tokens");
                builder.HasKey(t => t.Id);
                builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
                builder.Property(t => t.SessionId).HasColumnName("session_id");
                builder.Property(t => t.ConsumedAt).HasColumnName("consumed_at").IsConcurrencyToken();
            });
        }
    }
}
