using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// audit_events is append-only (business rule ح-187; NFR-SEC-009; repository ADR 0006
/// E39, §5.4, §9): UPDATE, DELETE, a zero-row UPDATE or DELETE, TRUNCATE,
/// TRUNCATE ... CASCADE, INSERT ... ON CONFLICT DO UPDATE and MERGE are all refused
/// with SQLSTATE 42501 and the trigger's name; INSERT succeeds. Runs as the table
/// owner (the test role), so it proves the trigger, not the grants (ADR 0006 §7.3).
/// </summary>
public class AuditEventsAppendOnlyTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string UpdateDelete = "trg_audit_events_forbid_update_delete";
    private const string Truncate = "trg_audit_events_forbid_truncate";

    private readonly PostgresTestDatabaseFixture _fixture;

    public AuditEventsAppendOnlyTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Insert_Succeeds()
    {
        await using var connection = await OpenAsync();
        var before = await CountAsync(connection);

        await AuditSql.InsertAsync(connection, AuditSql.SystemRow());
        await AuditSql.InsertAsync(connection, AuditSql.SystemRow());

        Assert.Equal(before + 2, await CountAsync(connection));
    }

    [Fact]
    public async Task Update_IsRefused_AndTheRowIsUnchanged()
    {
        await using var connection = await OpenAsync();
        var id = await SeedAsync(connection);
        var before = await CountAsync(connection);

        await AuditSql.FailsAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "UPDATE audit_events SET correlation_id = 'tampered' WHERE id = @id;", ("id", id)),
            "42501", UpdateDelete);

        Assert.Equal(1, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM audit_events WHERE id = @id AND correlation_id = 'corr-1';", ("id", id)));
    }

    [Fact]
    public async Task Update_OfEveryRow_IsRefused() =>
        await AuditSql.FailsAsync(
            async () =>
            {
                await using var connection = await SeededConnectionAsync();
                await PostgresAssert.ExecuteAsync(connection, null, "UPDATE audit_events SET reason = 'x';");
            },
            "42501", UpdateDelete);

    [Fact]
    public async Task Update_MatchingNoRow_IsStillRefused()
    {
        await using var connection = await OpenAsync();
        await SeedAsync(connection);
        var before = await CountAsync(connection);

        await AuditSql.FailsAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "UPDATE audit_events SET reason = 'x' WHERE false;"),
            "42501", UpdateDelete);
        await AuditSql.FailsAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "UPDATE audit_events SET reason = 'x' WHERE id = @id;", ("id", Guid.NewGuid())),
            "42501", UpdateDelete);
    }

    [Fact]
    public async Task Delete_IsRefused_AndTheRowRemains()
    {
        await using var connection = await OpenAsync();
        var id = await SeedAsync(connection);
        var before = await CountAsync(connection);

        await AuditSql.FailsAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM audit_events WHERE id = @id;", ("id", id)),
            "42501", UpdateDelete);

        Assert.Equal(before, await CountAsync(connection));
    }

    [Fact]
    public async Task Delete_OfEveryRow_AndOfNoRow_IsRefused()
    {
        await using var connection = await OpenAsync();
        await SeedAsync(connection);
        var before = await CountAsync(connection);

        await AuditSql.FailsAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM audit_events;"), "42501", UpdateDelete);
        await AuditSql.FailsAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM audit_events WHERE false;"), "42501", UpdateDelete);

        Assert.Equal(before, await CountAsync(connection));
    }

    [Fact]
    public async Task Truncate_IsRefused_AndRowsRemain()
    {
        await using var connection = await OpenAsync();
        await SeedAsync(connection);
        var before = await CountAsync(connection);

        await AuditSql.FailsAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "TRUNCATE audit_events;"), "42501", Truncate);

        Assert.Equal(before, await CountAsync(connection));
    }

    [Fact]
    public async Task TruncateOfAReferencedTable_Cascade_ReachesTheAuditTableAndIsRefused()
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);
        await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId));
        var before = await CountAsync(connection);

        await AuditSql.FailsAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "TRUNCATE users CASCADE;"), "42501", Truncate);

        Assert.Equal(before, await CountAsync(connection));
        Assert.Equal(1, await PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM users WHERE id = @id;", ("id", userId)));
    }

    [Fact]
    public async Task InsertOnConflictDoUpdate_IsRefused_EvenWhenNothingConflicts()
    {
        await using var connection = await OpenAsync();
        var existing = AuditSql.SystemRow();
        await AuditSql.InsertAsync(connection, existing);
        var before = await CountAsync(connection);
        var key = (string)existing["idempotency_key"]!;

        // Conflicting key: would update the existing row.
        await AuditSql.FailsAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, UpsertSql, UpsertParameters(AuditSql.SystemRow(), key)),
            "42501", UpdateDelete);

        // No conflict: the statement-level UPDATE trigger still fires for DO UPDATE.
        await AuditSql.FailsAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, UpsertSql, UpsertParameters(AuditSql.SystemRow(), "UserCreated:new-key")),
            "42501", UpdateDelete);

        Assert.Equal(before, await CountAsync(connection));
        Assert.Equal(0, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM audit_events WHERE reason IS NOT NULL;"));
    }

    [Fact]
    public async Task InsertOnConflictDoNothing_IsNotAnUpdate_AndDoesNotTouchTheRow()
    {
        // The writer never uses DO NOTHING (it could hide a lost audit row); this documents
        // that the database itself allows it, so the rule rests on the writer.
        await using var connection = await OpenAsync();
        var existing = AuditSql.SystemRow();
        await AuditSql.InsertAsync(connection, existing);
        var before = await CountAsync(connection);

        var inserted = await PostgresAssert.ExecuteAsync(
            connection, null,
            "INSERT INTO audit_events (id, occurred_at_utc, actor_type, action_type, entity_type, entity_id, correlation_id, source, idempotency_key, schema_version) " +
            "VALUES (@id, @at, 'System', 'UserCreated', 'User', @entity, 'c', 'SystemJob', @key, 1) " +
            "ON CONFLICT (idempotency_key) WHERE idempotency_key IS NOT NULL DO NOTHING;",
            ("id", Guid.CreateVersion7()), ("at", AuditSql.T0), ("entity", Guid.CreateVersion7()), ("key", existing["idempotency_key"]!));

        Assert.Equal(0, inserted);
        Assert.Equal(before, await CountAsync(connection));
    }

    [Fact]
    public async Task Merge_WithAnUpdateOrDeleteAction_IsRefused()
    {
        await using var connection = await OpenAsync();
        await SeedAsync(connection);
        var before = await CountAsync(connection);

        await AuditSql.FailsAsync(
            () => PostgresAssert.ExecuteAsync(connection, null,
                "MERGE INTO audit_events a USING (SELECT id FROM audit_events) s ON a.id = s.id WHEN MATCHED THEN UPDATE SET reason = 'x';"),
            "42501", UpdateDelete);
        await AuditSql.FailsAsync(
            () => PostgresAssert.ExecuteAsync(connection, null,
                "MERGE INTO audit_events a USING (SELECT id FROM audit_events) s ON a.id = s.id WHEN MATCHED THEN DELETE;"),
            "42501", UpdateDelete);

        Assert.Equal(before, await CountAsync(connection));
    }

    [Fact]
    public async Task RefusalMessage_NamesTheOperationAndTheRule()
    {
        await using var connection = await OpenAsync();
        await SeedAsync(connection);
        var before = await CountAsync(connection);

        var exception = await AuditSql.FailsAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM audit_events;"), "42501", UpdateDelete);

        Assert.Contains("DELETE", exception.Message);
        Assert.Contains("append-only", exception.Message);
    }

    [Fact]
    public async Task ARefusedStatement_InsideATransaction_AbortsTheWholeTransaction()
    {
        await using var connection = await OpenAsync();
        await SeedAsync(connection);
        var before = await CountAsync(connection);

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await AuditSql.InsertAsync(connection, AuditSql.SystemRow());
            await Assert.ThrowsAsync<PostgresException>(
                () => PostgresAssert.ExecuteAsync(connection, transaction, "DELETE FROM audit_events;"));
            await transaction.RollbackAsync();
        }

        Assert.Equal(before, await CountAsync(connection));
    }

    // ---- helpers ---------------------------------------------------------

    private const string UpsertSql =
        "INSERT INTO audit_events (id, occurred_at_utc, actor_type, action_type, entity_type, entity_id, correlation_id, source, idempotency_key, schema_version) " +
        "VALUES (@id, @at, 'System', 'UserCreated', 'User', @entity, 'c', 'SystemJob', @key, 1) " +
        "ON CONFLICT (idempotency_key) WHERE idempotency_key IS NOT NULL DO UPDATE SET reason = 'tampered';";

    private static (string Name, object Value)[] UpsertParameters(Dictionary<string, object?> row, string key) =>
        [("id", row["id"]!), ("at", AuditSql.T0), ("entity", row["entity_id"]!), ("key", key)];

    private static Task<long> CountAsync(NpgsqlConnection connection) =>
        PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM audit_events;");

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task<NpgsqlConnection> SeededConnectionAsync()
    {
        var connection = await OpenAsync();
        await SeedAsync(connection);
        return connection;
    }

    private static async Task<Guid> SeedAsync(NpgsqlConnection connection)
    {
        var row = AuditSql.SystemRow();
        await AuditSql.InsertAsync(connection, row);
        return (Guid)row["id"]!;
    }

    private static async Task<Guid> InsertUserAsync(NpgsqlConnection connection)
    {
        var id = Guid.CreateVersion7();
        await PostgresAssert.ExecuteAsync(connection, null,
            $"INSERT INTO users ({TestUsers.RawInsertColumns}) VALUES (@id, @login, 'Active', 'hash', false, 1);",
            ("id", id), ("login", $"audit.{id:N}"));
        return id;
    }
}
