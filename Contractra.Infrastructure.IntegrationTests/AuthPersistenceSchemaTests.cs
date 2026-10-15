using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Slice 2B schema (US-BE-008, ADR 0005 §3): exact columns (hashes only, no
/// raw token or code column), named constraints and indexes, RESTRICT foreign
/// keys, deferred exclusion constraints, and each CHECK enforced by PostgreSQL.
/// Uses raw SQL on purpose, to test the database rather than the Domain.
/// </summary>
public class AuthPersistenceSchemaTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgresTestDatabaseFixture _fixture;

    public AuthPersistenceSchemaTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("auth_sessions", "absolute_expires_at:timestamp with time zone,client_app:text,client_platform:text,created_at:timestamp with time zone,id:uuid,last_refreshed_at:timestamp with time zone,revoke_reason:text,revoked_at:timestamp with time zone,user_id:uuid")]
    [InlineData("refresh_tokens", "consumed_at:timestamp with time zone,id:uuid,issued_at:timestamp with time zone,session_id:uuid,token_hash:bytea")]
    [InlineData("password_reset_tickets", "channel:text,code_hash:bytea,consumed_at:timestamp with time zone,expires_at:timestamp with time zone,id:uuid,issued_at:timestamp with time zone,issued_by_user_id:uuid,superseded_at:timestamp with time zone,user_id:uuid")]
    [InlineData("auth_throttle_counters", "blocked_until:timestamp with time zone,failure_count:integer,key_hash:bytea,scope:text,window_start:timestamp with time zone")]
    public async Task Table_HasExactlyTheseColumns_SoNoRawTokenOrCodeIsStored(string table, string expected)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT string_agg(column_name || ':' || data_type, ',' ORDER BY column_name) FROM information_schema.columns " +
            "WHERE table_schema = current_schema() AND table_name = @table;", connection);
        command.Parameters.AddWithValue("table", table);

        Assert.Equal(expected, (string?)await command.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("pk_auth_sessions")]
    [InlineData("fk_auth_sessions_users_user_id")]
    [InlineData("ck_auth_sessions_client_app_valid_values")]
    [InlineData("ck_auth_sessions_client_platform_valid_values")]
    [InlineData("ck_auth_sessions_absolute_expiry_after_creation")]
    [InlineData("ck_auth_sessions_last_refreshed_not_before_creation")]
    [InlineData("ck_auth_sessions_revocation_fields_together")]
    [InlineData("ck_auth_sessions_revoked_not_before_creation")]
    [InlineData("ck_auth_sessions_revoke_reason_valid_values")]
    [InlineData("pk_refresh_tokens")]
    [InlineData("fk_refresh_tokens_auth_sessions_session_id")]
    [InlineData("ck_refresh_tokens_token_hash_length")]
    [InlineData("ck_refresh_tokens_consumed_not_before_issued")]
    [InlineData("ex_refresh_tokens_one_unconsumed_per_session")]
    [InlineData("pk_password_reset_tickets")]
    [InlineData("fk_password_reset_tickets_users_user_id")]
    [InlineData("fk_password_reset_tickets_users_issued_by_user_id")]
    [InlineData("ck_password_reset_tickets_code_hash_length")]
    [InlineData("ck_password_reset_tickets_expires_after_issued")]
    [InlineData("ck_password_reset_tickets_channel_valid")]
    [InlineData("ck_password_reset_tickets_consumed_not_before_issued")]
    [InlineData("ck_password_reset_tickets_consumed_before_expiry")]
    [InlineData("ck_password_reset_tickets_superseded_not_before_issued")]
    [InlineData("ck_password_reset_tickets_not_consumed_and_superseded")]
    [InlineData("ex_password_reset_tickets_one_open_per_user")]
    [InlineData("pk_auth_throttle_counters")]
    [InlineData("ck_auth_throttle_counters_scope_valid_values")]
    [InlineData("ck_auth_throttle_counters_key_hash_length")]
    [InlineData("ck_auth_throttle_counters_failure_count_positive")]
    [InlineData("ck_auth_throttle_counters_blocked_until_not_before_window_start")]
    public async Task Constraint_Exists(string name)
    {
        await using var connection = await OpenAsync();

        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM pg_constraint WHERE conname = @name;", ("name", name)));
    }

    [Theory]
    [InlineData("ix_auth_sessions_user_id_active", "auth_sessions", "CREATE INDEX ix_auth_sessions_user_id_active ON public.auth_sessions USING btree (user_id) WHERE (revoked_at IS NULL)")]
    [InlineData("ux_refresh_tokens_token_hash", "refresh_tokens", "CREATE UNIQUE INDEX ux_refresh_tokens_token_hash ON public.refresh_tokens USING btree (token_hash)")]
    [InlineData("ix_refresh_tokens_session_id", "refresh_tokens", "CREATE INDEX ix_refresh_tokens_session_id ON public.refresh_tokens USING btree (session_id)")]
    [InlineData("ix_password_reset_tickets_user_id_issued_at", "password_reset_tickets", "CREATE INDEX ix_password_reset_tickets_user_id_issued_at ON public.password_reset_tickets USING btree (user_id, issued_at DESC)")]
    [InlineData("ix_password_reset_tickets_issued_by_user_id", "password_reset_tickets", "CREATE INDEX ix_password_reset_tickets_issued_by_user_id ON public.password_reset_tickets USING btree (issued_by_user_id)")]
    [InlineData("ix_auth_throttle_counters_window_start", "auth_throttle_counters", "CREATE INDEX ix_auth_throttle_counters_window_start ON public.auth_throttle_counters USING btree (window_start)")]
    public async Task Index_HasThisDefinition(string name, string table, string definition)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE indexname = @name AND tablename = @table;", connection);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("table", table);

        Assert.Equal(definition, (string?)await command.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("auth_sessions", "ix_auth_sessions_user_id_active,pk_auth_sessions")]
    [InlineData("refresh_tokens", "ex_refresh_tokens_one_unconsumed_per_session,ix_refresh_tokens_session_id,pk_refresh_tokens,ux_refresh_tokens_token_hash")]
    [InlineData("password_reset_tickets", "ex_password_reset_tickets_one_open_per_user,ix_password_reset_tickets_issued_by_user_id,ix_password_reset_tickets_user_id_issued_at,pk_password_reset_tickets")]
    [InlineData("auth_throttle_counters", "ix_auth_throttle_counters_window_start,pk_auth_throttle_counters")]
    public async Task Table_HasExactlyTheseIndexes(string table, string expected)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT string_agg(indexname, ',' ORDER BY indexname) FROM pg_indexes WHERE schemaname = current_schema() AND tablename = @table;",
            connection);
        command.Parameters.AddWithValue("table", table);

        Assert.Equal(expected, (string?)await command.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("ex_refresh_tokens_one_unconsumed_per_session", "EXCLUDE USING btree (session_id WITH =) WHERE ((consumed_at IS NULL)) DEFERRABLE INITIALLY DEFERRED")]
    [InlineData("ex_password_reset_tickets_one_open_per_user", "EXCLUDE USING btree (user_id WITH =) WHERE (((consumed_at IS NULL) AND (superseded_at IS NULL))) DEFERRABLE INITIALLY DEFERRED")]
    public async Task ExclusionConstraint_IsPartialAndDeferredToCommit(string name, string definition)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT contype::text, condeferrable, condeferred, pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = @name;",
            connection);
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("x", reader.GetString(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.Equal(definition, reader.GetString(3));
    }

    [Theory]
    [InlineData("fk_auth_sessions_users_user_id")]
    [InlineData("fk_refresh_tokens_auth_sessions_session_id")]
    [InlineData("fk_password_reset_tickets_users_user_id")]
    [InlineData("fk_password_reset_tickets_users_issued_by_user_id")]
    public async Task ForeignKey_IsOnDeleteRestrict(string name)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT confdeltype::text FROM pg_constraint WHERE conname = @name AND contype = 'f';", connection);
        command.Parameters.AddWithValue("name", name);

        Assert.Equal("r", (string?)await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task DeletingAUserWithSessionsOrTickets_IsRejected_AndNothingIsDeleted()
    {
        await using var connection = await OpenAsync();
        var withSession = await InsertUserAsync(connection);
        var withTicket = await InsertUserAsync(connection);
        var session = await InsertSessionAsync(connection, withSession);
        await InsertTokenAsync(connection, session, Hash(1));
        await InsertTicketAsync(connection, withTicket, Hash(2));

        await AssertRestrictAsync(() => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM users WHERE id = @id;", ("id", withSession)));
        await AssertRestrictAsync(() => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM users WHERE id = @id;", ("id", withTicket)));
        await AssertRestrictAsync(() => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM auth_sessions WHERE id = @id;", ("id", session)));

        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM auth_sessions WHERE id = @id;", ("id", session)));
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM refresh_tokens WHERE session_id = @id;", ("id", session)));
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM password_reset_tickets WHERE user_id = @id;", ("id", withTicket)));
    }

    [Theory]
    [InlineData("client_app = 'Admin'", "ck_auth_sessions_client_app_valid_values")]
    [InlineData("client_platform = 'desktop'", "ck_auth_sessions_client_platform_valid_values")]
    [InlineData("absolute_expires_at = created_at", "ck_auth_sessions_absolute_expiry_after_creation")]
    [InlineData("last_refreshed_at = created_at - interval '1 second'", "ck_auth_sessions_last_refreshed_not_before_creation")]
    [InlineData("revoked_at = created_at", "ck_auth_sessions_revocation_fields_together")]
    [InlineData("revoke_reason = 'logout'", "ck_auth_sessions_revocation_fields_together")]
    [InlineData("revoked_at = created_at - interval '1 second', revoke_reason = 'logout'", "ck_auth_sessions_revoked_not_before_creation")]
    [InlineData("revoked_at = created_at, revoke_reason = 'expired'", "ck_auth_sessions_revoke_reason_valid_values")]
    public async Task SessionCheck_IsEnforced(string assignment, string constraint)
    {
        await using var connection = await OpenAsync();
        var session = await InsertSessionAsync(connection, await InsertUserAsync(connection));

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, $"UPDATE auth_sessions SET {assignment} WHERE id = @id;", ("id", session)),
            "23514", constraint);
    }

    [Theory]
    [InlineData("logout")]
    [InlineData("idle")]
    [InlineData("refresh_reuse")]
    [InlineData("app_access_lost")]
    [InlineData("user_disabled")]
    [InlineData("password_reset_issued")]
    [InlineData("password_reset_completed")]
    [InlineData("initial_password_reissued")]
    [InlineData("password_changed")]
    public async Task SessionRevokeReason_AcceptsEachDocumentedCode(string code)
    {
        await using var connection = await OpenAsync();
        var session = await InsertSessionAsync(connection, await InsertUserAsync(connection));

        Assert.Equal(1, await PostgresAssert.ExecuteAsync(connection, null,
            "UPDATE auth_sessions SET revoked_at = created_at, revoke_reason = @code WHERE id = @id;", ("code", code), ("id", session)));
    }

    [Fact]
    public async Task RefreshTokenChecks_AreEnforced()
    {
        await using var connection = await OpenAsync();
        var session = await InsertSessionAsync(connection, await InsertUserAsync(connection));

        await PostgresAssert.FailsWithAsync(() => InsertTokenAsync(connection, session, new byte[31]), "23514", "ck_refresh_tokens_token_hash_length");

        var token = await InsertTokenAsync(connection, session, Hash(10));
        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(connection, null,
                "UPDATE refresh_tokens SET consumed_at = issued_at - interval '1 second' WHERE id = @id;", ("id", token)),
            "23514", "ck_refresh_tokens_consumed_not_before_issued");
    }

    [Fact]
    public async Task RefreshTokenHash_IsUniqueAcrossSessions_IncludingConsumedTokens()
    {
        await using var connection = await OpenAsync();
        var user = await InsertUserAsync(connection);
        var first = await InsertSessionAsync(connection, user);
        var second = await InsertSessionAsync(connection, user);
        var hash = Hash(11);
        var token = await InsertTokenAsync(connection, first, hash);
        await PostgresAssert.ExecuteAsync(connection, null, "UPDATE refresh_tokens SET consumed_at = issued_at WHERE id = @id;", ("id", token));

        await PostgresAssert.FailsWithAsync(() => InsertTokenAsync(connection, second, hash), "23505", "ux_refresh_tokens_token_hash");
    }

    [Theory]
    [InlineData("expires_at = issued_at", "ck_password_reset_tickets_expires_after_issued")]
    [InlineData("channel = ''", "ck_password_reset_tickets_channel_valid")]
    [InlineData("channel = repeat('c', 33)", "ck_password_reset_tickets_channel_valid")]
    [InlineData("consumed_at = issued_at - interval '1 second'", "ck_password_reset_tickets_consumed_not_before_issued")]
    [InlineData("consumed_at = expires_at", "ck_password_reset_tickets_consumed_before_expiry")]
    [InlineData("superseded_at = issued_at - interval '1 second'", "ck_password_reset_tickets_superseded_not_before_issued")]
    [InlineData("consumed_at = issued_at, superseded_at = issued_at", "ck_password_reset_tickets_not_consumed_and_superseded")]
    public async Task TicketCheck_IsEnforced(string assignment, string constraint)
    {
        await using var connection = await OpenAsync();
        var ticket = await InsertTicketAsync(connection, await InsertUserAsync(connection), Hash(20));

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, $"UPDATE password_reset_tickets SET {assignment} WHERE id = @id;", ("id", ticket)),
            "23514", constraint);
    }

    [Fact]
    public async Task TicketCodeHash_MustBeSha256Sized()
    {
        await using var connection = await OpenAsync();

        var user = await InsertUserAsync(connection);

        await PostgresAssert.FailsWithAsync(
            () => InsertTicketAsync(connection, user, new byte[33]), "23514", "ck_password_reset_tickets_code_hash_length");
    }

    [Theory]
    [InlineData("login_pair")]
    [InlineData("identifier")]
    [InlineData("change_password_user")]
    public async Task ThrottleScope_AcceptsEachLayerCode(string scope)
    {
        await using var connection = await OpenAsync();

        Assert.Equal(1, await InsertCounterAsync(connection, scope, Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray(), 1, null));
    }

    [Fact]
    public async Task ThrottleChecks_AreEnforced()
    {
        await using var connection = await OpenAsync();

        await PostgresAssert.FailsWithAsync(() => InsertCounterAsync(connection, "login", Hash(30), 1, null), "23514", "ck_auth_throttle_counters_scope_valid_values");
        await PostgresAssert.FailsWithAsync(() => InsertCounterAsync(connection, "identifier", new byte[16], 1, null), "23514", "ck_auth_throttle_counters_key_hash_length");
        await PostgresAssert.FailsWithAsync(() => InsertCounterAsync(connection, "identifier", Hash(31), 0, null), "23514", "ck_auth_throttle_counters_failure_count_positive");
        await PostgresAssert.FailsWithAsync(() => InsertCounterAsync(connection, "identifier", Hash(32), 1, T0.AddSeconds(-1)), "23514", "ck_auth_throttle_counters_blocked_until_not_before_window_start");

        var key = Hash(33);
        await InsertCounterAsync(connection, "identifier", key, 1, null);
        await PostgresAssert.FailsWithAsync(() => InsertCounterAsync(connection, "identifier", key, 1, null), "23505", "pk_auth_throttle_counters");
    }

    // RESTRICT reports 23001 on PostgreSQL 18 and 23503 on PostgreSQL 16; new
    // tests must not depend on the difference (sprint-02 §E).
    private static async Task AssertRestrictAsync(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Contains(exception.SqlState, new[] { "23001", "23503" });
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>A fresh random 32-byte hash whose first byte is <paramref name="seed"/>.</summary>
    internal static byte[] Hash(byte seed)
    {
        var hash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();
        hash[0] = seed;
        return hash;
    }

    internal static async Task<Guid> InsertUserAsync(NpgsqlConnection connection)
    {
        var id = Guid.CreateVersion7();
        await PostgresAssert.ExecuteAsync(connection, null,
            $"INSERT INTO users ({TestUsers.RawInsertColumns}) VALUES (@id, @login, 'Active', @hash, false, 1);",
            ("id", id), ("login", $"auth.{id:N}"), ("hash", TestUsers.Hash));
        return id;
    }

    internal static async Task<Guid> InsertSessionAsync(NpgsqlConnection connection, Guid userId)
    {
        var id = Guid.CreateVersion7();
        await PostgresAssert.ExecuteAsync(connection, null,
            "INSERT INTO auth_sessions (id, user_id, client_app, client_platform, created_at, absolute_expires_at, last_refreshed_at) " +
            "VALUES (@id, @user, 'employee', 'mobile', @created, @expires, @created);",
            ("id", id), ("user", userId), ("created", T0), ("expires", T0.AddDays(7)));
        return id;
    }

    internal static async Task<Guid> InsertTokenAsync(NpgsqlConnection connection, Guid sessionId, byte[] hash)
    {
        var id = Guid.CreateVersion7();
        await PostgresAssert.ExecuteAsync(connection, null,
            "INSERT INTO refresh_tokens (id, session_id, token_hash, issued_at) VALUES (@id, @session, @hash, @issued);",
            ("id", id), ("session", sessionId), ("hash", hash), ("issued", T0));
        return id;
    }

    internal static async Task<Guid> InsertTicketAsync(NpgsqlConnection connection, Guid userId, byte[] hash)
    {
        var id = Guid.CreateVersion7();
        await PostgresAssert.ExecuteAsync(connection, null,
            "INSERT INTO password_reset_tickets (id, user_id, code_hash, issued_at, expires_at, channel) " +
            "VALUES (@id, @user, @hash, @issued, @expires, 'operator');",
            ("id", id), ("user", userId), ("hash", hash), ("issued", T0), ("expires", T0.AddHours(24)));
        return id;
    }

    private static Task<int> InsertCounterAsync(NpgsqlConnection connection, string scope, byte[] keyHash, int failureCount, DateTimeOffset? blockedUntil)
    {
        var command = "INSERT INTO auth_throttle_counters (scope, key_hash, window_start, failure_count, blocked_until) " +
                      "VALUES (@scope, @key, @window, @count, @blocked);";
        return PostgresAssert.ExecuteAsync(connection, null, command,
            ("scope", scope), ("key", keyHash), ("window", T0), ("count", failureCount), ("blocked", (object?)blockedUntil ?? DBNull.Value));
    }

    private static Task<long> CountAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters) =>
        PostgresAssert.CountAsync(connection, null, sql, parameters);
}
