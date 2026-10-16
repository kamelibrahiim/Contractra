using System.Text.RegularExpressions;
using Contractra.Domain.Audit;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// audit_events schema (US-BE-009; repository ADR 0006 §5.1, §9): exact columns,
/// named constraints, every CHECK enforced by PostgreSQL (accepted and rejected),
/// RESTRICT foreign keys, the global partial unique index, and catalog CHECKs equal
/// to the Domain enums. Raw SQL on purpose.
/// </summary>
public class AuditEventsSchemaTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public AuditEventsSchemaTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    // ---- structure -------------------------------------------------------

    [Fact]
    public async Task Table_HasExactlyTheseColumns_AndNoIpUserAgentPayloadOrClientEventId()
    {
        var expected = new[]
        {
            "action_type:text", "actor_role_code:text", "actor_type:text", "actor_user_id:uuid", "approved_by_user_id:uuid",
            "business_document_number:text", "causation_id:text", "correlation_id:text", "entity_id:uuid", "entity_type:text",
            "id:uuid", "idempotency_key:text", "managed_company_id:uuid", "new_status:text", "occurred_at_utc:timestamp with time zone",
            "operation_id:uuid", "previous_status:text", "reason:text", "schema_version:integer", "site_id:uuid", "snapshot:jsonb",
            "source:text",
        };

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT column_name || ':' || data_type FROM information_schema.columns " +
            "WHERE table_schema = current_schema() AND table_name = 'audit_events';", connection);
        var actual = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                actual.Add(reader.GetString(0));
            }
        }

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Table_NullabilityMatchesTheDesign()
    {
        var notNull = new[]
        {
            "action_type", "actor_type", "correlation_id", "entity_id", "entity_type", "id", "occurred_at_utc", "schema_version", "source",
        };

        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() " +
            "AND table_name = 'audit_events' AND is_nullable = 'NO';", connection);
        var actual = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                actual.Add(reader.GetString(0));
            }
        }

        Assert.Equal(notNull.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Table_HasNoColumnDefaults_EveryRowStatesEveryValue()
    {
        await using var connection = await OpenAsync();

        Assert.Equal(0, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = current_schema() " +
            "AND table_name = 'audit_events' AND column_default IS NOT NULL;"));
    }

    [Theory]
    [InlineData("pk_audit_events")]
    [InlineData("fk_audit_events_users_actor_user_id")]
    [InlineData("fk_audit_events_users_approved_by_user_id")]
    [InlineData("ck_audit_events_actor_type_valid_values")]
    [InlineData("ck_audit_events_actor_role_code_valid")]
    [InlineData("ck_audit_events_actor_consistency")]
    [InlineData("ck_audit_events_action_type_valid_values")]
    [InlineData("ck_audit_events_entity_type_valid")]
    [InlineData("ck_audit_events_business_document_number_valid")]
    [InlineData("ck_audit_events_context_not_yet_defined")]
    [InlineData("ck_audit_events_status_pair")]
    [InlineData("ck_audit_events_status_changed")]
    [InlineData("ck_audit_events_status_valid")]
    [InlineData("ck_audit_events_reason_valid")]
    [InlineData("ck_audit_events_approved_by_not_yet_defined")]
    [InlineData("ck_audit_events_correlation_id_format")]
    [InlineData("ck_audit_events_causation_id_format")]
    [InlineData("ck_audit_events_source_valid_values")]
    [InlineData("ck_audit_events_system_actor_source")]
    [InlineData("ck_audit_events_idempotency_key_format")]
    [InlineData("ck_audit_events_idempotency_key_length")]
    [InlineData("ck_audit_events_idempotency_key_required_for_source")]
    [InlineData("ck_audit_events_idempotency_key_prefix")]
    [InlineData("ck_audit_events_snapshot_valid")]
    [InlineData("ck_audit_events_schema_version_positive")]
    public async Task Constraint_Exists(string name)
    {
        await using var connection = await OpenAsync();

        Assert.Equal(1, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM pg_constraint WHERE conname = @name AND conrelid = to_regclass('audit_events');", ("name", name)));
    }

    [Theory]
    [InlineData("ux_audit_events_idempotency_key")]
    [InlineData("ix_audit_events_actor_user_id")]
    [InlineData("ix_audit_events_approved_by_user_id")]
    [InlineData("ix_audit_events_entity")]
    public async Task Index_Exists(string name)
    {
        await using var connection = await OpenAsync();

        Assert.Equal(1, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM pg_indexes WHERE indexname = @name AND tablename = 'audit_events';", ("name", name)));
    }

    [Fact]
    public async Task IdempotencyKeyIndex_IsGlobalPartialUnique()
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_audit_events_idempotency_key';", connection);
        var definition = (string)(await command.ExecuteScalarAsync())!;

        Assert.Contains("UNIQUE", definition);
        Assert.Contains("(idempotency_key)", definition);
        Assert.Contains("WHERE (idempotency_key IS NOT NULL)", definition);
    }

    [Fact]
    public async Task TheTableHasExactlyTwentyFiveConstraints()
    {
        // 1 primary key + 2 foreign keys + 22 CHECKs (idempotency_key's length and
        // format are now separate constraints). NOT NULL constraints are excluded:
        // PostgreSQL 18 records them in pg_constraint (contype 'n'), PostgreSQL 16 does not.
        await using var connection = await OpenAsync();

        Assert.Equal(25, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM pg_constraint WHERE conrelid = to_regclass('audit_events') AND contype <> 'n';"));
    }

    [Theory]
    [InlineData("fk_audit_events_users_actor_user_id")]
    [InlineData("fk_audit_events_users_approved_by_user_id")]
    public async Task ForeignKeys_ReferenceUsers_OnDeleteRestrict(string name)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT confrelid::regclass::text, confdeltype::text FROM pg_constraint WHERE conname = @name;", connection);
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("users", reader.GetString(0));
        Assert.Equal("r", reader.GetString(1));
    }

    [Theory]
    [InlineData("ck_audit_events_actor_type_valid_values", typeof(AuditActorType))]
    [InlineData("ck_audit_events_action_type_valid_values", typeof(AuditActionType))]
    [InlineData("ck_audit_events_source_valid_values", typeof(AuditSource))]
    public async Task CatalogChecks_EqualTheDomainEnums(string constraint, Type enumType)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = @name;", connection);
        command.Parameters.AddWithValue("name", constraint);
        var definition = (string)(await command.ExecuteScalarAsync())!;

        var codes = Regex.Matches(definition, "'([A-Za-z]+)'").Select(m => m.Groups[1].Value).Distinct().ToArray();

        Assert.Equal(Enum.GetNames(enumType).Order(StringComparer.Ordinal), codes.Order(StringComparer.Ordinal));
    }

    // ---- accepted rows ---------------------------------------------------

    [Fact]
    public async Task ValidSystemAndUserRows_AreAccepted()
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);
        var before = await CountAsync(connection);

        await AuditSql.InsertAsync(connection, AuditSql.SystemRow());
        await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId));
        await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(
            ("action_type", "UserDisabled"), ("previous_status", "Active"), ("new_status", "Disabled"), ("reason", "Left the company"),
            ("snapshot", "{\"role_code\":\"Admin\"}"), ("source", "EmployeeApp"), ("idempotency_key", "UserDisabled:op-1:sub-1")));

        Assert.Equal(before + 3, await CountAsync(connection));
    }

    [Fact]
    public async Task EveryActionAndSourceCode_IsAccepted()
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);
        var before = await CountAsync(connection);

        foreach (var action in Enum.GetNames<AuditActionType>())
        {
            await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(("action_type", action)));
        }

        foreach (var source in Enum.GetNames<AuditSource>())
        {
            await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(
                ("source", source), ("idempotency_key", $"PasswordChanged:{source}-key")));
        }

        Assert.Equal(before + Enum.GetNames<AuditActionType>().Length + Enum.GetNames<AuditSource>().Length,
            await CountAsync(connection));
    }

    [Fact]
    public async Task BoundaryValues_AreAccepted()
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);
        var before = await CountAsync(connection);

        await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(
            ("correlation_id", new string('a', 64)), ("causation_id", "A-b_c.1"), ("reason", new string('r', 1000)),
            ("business_document_number", new string('d', 128)), ("entity_type", "U" + new string('x', 63)),
            ("idempotency_key", "PasswordChanged:" + new string('k', 240))));

        Assert.Equal(before + 1, await CountAsync(connection));
    }

    // ---- rejected rows (each violates exactly the named constraint) -----

    public static TheoryData<string, string, object?> RejectedValues => new()
    {
        // Violating the actor_type list also violates the actor rule; PostgreSQL reports the first by name.
        { "ck_audit_events_actor_consistency", "actor_type", "Robot" },
        { "ck_audit_events_action_type_valid_values", "action_type", "PasswordReset" },
        { "ck_audit_events_action_type_valid_values", "action_type", "LoginFailed" },
        { "ck_audit_events_action_type_valid_values", "action_type", "UserDeactivated" },
        { "ck_audit_events_source_valid_values", "source", "PWA" },
        { "ck_audit_events_actor_role_code_valid", "actor_role_code", "" },
        { "ck_audit_events_actor_role_code_valid", "actor_role_code", " Admin" },
        { "ck_audit_events_actor_role_code_valid", "actor_role_code", "Admin " },
        { "ck_audit_events_entity_type_valid", "entity_type", "user" },
        { "ck_audit_events_entity_type_valid", "entity_type", "1User" },
        { "ck_audit_events_entity_type_valid", "entity_type", "U" + new string('x', 64) },
        { "ck_audit_events_business_document_number_valid", "business_document_number", "" },
        { "ck_audit_events_business_document_number_valid", "business_document_number", " x" },
        { "ck_audit_events_business_document_number_valid", "business_document_number", new string('d', 129) },
        { "ck_audit_events_status_pair", "previous_status", "Active" },
        { "ck_audit_events_status_pair", "new_status", "Disabled" },
        { "ck_audit_events_reason_valid", "reason", "" },
        { "ck_audit_events_reason_valid", "reason", " padded" },
        { "ck_audit_events_correlation_id_format", "correlation_id", "" },
        { "ck_audit_events_correlation_id_format", "correlation_id", "has space" },
        { "ck_audit_events_correlation_id_format", "correlation_id", "slash/" },
        { "ck_audit_events_causation_id_format", "causation_id", "has space" },
        { "ck_audit_events_causation_id_format", "causation_id", "" },
        { "ck_audit_events_context_not_yet_defined", "managed_company_id", "11111111-1111-1111-1111-111111111111" },
        { "ck_audit_events_context_not_yet_defined", "operation_id", "11111111-1111-1111-1111-111111111111" },
        { "ck_audit_events_context_not_yet_defined", "site_id", "11111111-1111-1111-1111-111111111111" },
        { "ck_audit_events_approved_by_not_yet_defined", "approved_by_user_id", "USER" },
        { "ck_audit_events_idempotency_key_format", "idempotency_key", "PasswordChanged:has space" },
        { "ck_audit_events_idempotency_key_format", "idempotency_key", "PasswordChanged:é" },
        { "ck_audit_events_idempotency_key_format", "idempotency_key", "PasswordChanged:\t" },
        { "ck_audit_events_idempotency_key_length", "idempotency_key", "PasswordChanged:" + new string('k', 241) },
        { "ck_audit_events_idempotency_key_prefix", "idempotency_key", "RoleEnded:x" },
        { "ck_audit_events_idempotency_key_prefix", "idempotency_key", "PasswordChangedX:x" },
        { "ck_audit_events_idempotency_key_prefix", "idempotency_key", "x" },
        { "ck_audit_events_schema_version_positive", "schema_version", 0 },
        { "ck_audit_events_schema_version_positive", "schema_version", -1 },
    };

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public async Task InvalidValue_IsRejectedByTheNamedCheck(string constraint, string column, object? value)
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);
        if (value is string text && text.Length == 36 && Guid.TryParse(text, out var guid))
        {
            value = guid;
        }

        if (value is "USER")
        {
            value = userId;
        }

        var before = await CountAsync(connection);
        var row = AuditSql.UserRow(userId).With((column, value));
        if (column == "previous_status" || column == "new_status")
        {
            row = row.With(("previous_status", column == "previous_status" ? value : null), ("new_status", column == "new_status" ? value : null));
        }

        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, row), "23514", constraint);
        Assert.Equal(before, await CountAsync(connection));
    }

    [Fact]
    public async Task StatusChange_MustChange_AndBeTrimmedAndNonEmpty()
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);

        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(
            ("previous_status", "Active"), ("new_status", "Active"))), "23514", "ck_audit_events_status_changed");
        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(
            ("previous_status", " Active"), ("new_status", "Disabled"))), "23514", "ck_audit_events_status_valid");
        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(
            ("previous_status", ""), ("new_status", "Disabled"))), "23514", "ck_audit_events_status_valid");
    }

    [Fact]
    public async Task ActorRule_UserNeedsAUser_SystemHasNoUserAndNoRole()
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);
        const string rule = "ck_audit_events_actor_consistency";

        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(("actor_user_id", null))), "23514", rule);
        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.SystemRow().With(("actor_role_code", "Admin"))), "23514", rule);
        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.SystemRow().With(("actor_user_id", userId))), "23514", rule);

        // A user with no active role is allowed (contract §14.1).
        await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(("actor_role_code", null)));
    }

    [Fact]
    public async Task SystemActor_MustUseTheSystemJobSource()
    {
        await using var connection = await OpenAsync();

        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.SystemRow().With(("source", "Web"))),
            "23514", "ck_audit_events_system_actor_source");
        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.SystemRow().With(("source", "EmployeeApp"))),
            "23514", "ck_audit_events_system_actor_source");
    }

    [Theory]
    [InlineData("SystemJob")]
    [InlineData("OfflineSync")]
    [InlineData("Import")]
    public async Task IdempotencyKey_IsRequired_ForSyncImportAndJobSources(string source)
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);

        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(("source", source))),
            "23514", "ck_audit_events_idempotency_key_required_for_source");
    }

    [Theory]
    [InlineData("Web")]
    [InlineData("EmployeeApp")]
    public async Task IdempotencyKey_IsOptional_ForOnlineSources(string source)
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);

        await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(("source", source)));
    }

    [Fact]
    public async Task IdempotencyKey_FullKeyOfMoreThan256Characters_IsRejected()
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);

        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(
            ("idempotency_key", "PasswordChanged:" + new string('k', 242)))), "23514", "ck_audit_events_idempotency_key_length");
    }

    [Fact]
    public async Task IdempotencyKey_OfExactlyOneCharacter_IsAccepted()
    {
        // The shortest possible key: the action-type prefix with an empty operation
        // key. ("PasswordChanged:" is itself 16 characters - a literal 1-character
        // key would violate the prefix CHECK, not the length one - so this and the
        // 255/256 cases below all vary the filler after a fixed, valid prefix.)
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);

        await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(("idempotency_key", "PasswordChanged:")));
    }

    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    public async Task IdempotencyKey_UpToTheMaximumLength_IsAccepted(int fullKeyLength)
    {
        // "PasswordChanged:" is 16 characters; the operation key fills the rest.
        // The filler is 'm', not 'k', so this key never collides with the fixed
        // 256-character key BoundaryValues_AreAccepted inserts in this same database.
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);
        const string prefix = "PasswordChanged:";
        var key = prefix + new string('m', fullKeyLength - prefix.Length);
        Assert.Equal(fullKeyLength, key.Length);

        await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(("idempotency_key", key)));
    }

    [Fact]
    public async Task IdempotencyKey_OfExactlyTwoHundredAndFiftySevenCharacters_IsRejectedByLengthNotFormat()
    {
        // One character past the maximum: still every character is valid printable
        // ASCII with the right prefix, so only the length CHECK can reject it - this
        // guards against the two CHECKs being merged back into one bounded regex,
        // which PostgreSQL cannot evaluate above 255 repetitions (SQLSTATE 2201B).
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);
        var key = "PasswordChanged:" + new string('k', 241);
        Assert.Equal(257, key.Length);

        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(
            ("idempotency_key", key))), "23514", "ck_audit_events_idempotency_key_length");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    public async Task Snapshot_MustBeAJsonObject(string json)
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);

        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(("snapshot", json))),
            "23514", "ck_audit_events_snapshot_valid");
    }

    [Fact]
    public async Task Snapshot_IsLimitedTo8KiB()
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);
        var atLimit = "{\"k\":\"" + new string('v', 8192 - 10) + "\"}";
        var overLimit = "{\"k\":\"" + new string('v', 8192) + "\"}";

        await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(("snapshot", atLimit)));
        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(("snapshot", overLimit))),
            "23514", "ck_audit_events_snapshot_valid");
    }

    [Theory]
    [InlineData("id")]
    [InlineData("occurred_at_utc")]
    [InlineData("actor_type")]
    [InlineData("action_type")]
    [InlineData("entity_type")]
    [InlineData("entity_id")]
    [InlineData("correlation_id")]
    [InlineData("source")]
    [InlineData("schema_version")]
    public async Task RequiredColumns_CannotBeNull(string column)
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);

        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With((column, null))), "23502");
    }

    // ---- foreign keys ----------------------------------------------------

    [Fact]
    public async Task ActorUserId_MustReferenceAnExistingUser()
    {
        await using var connection = await OpenAsync();

        var exception = await AuditSql.FailsAsync(
            () => AuditSql.InsertAsync(connection, AuditSql.UserRow(Guid.CreateVersion7())),
            "23503", "fk_audit_events_users_actor_user_id");
        Assert.Equal("fk_audit_events_users_actor_user_id", ((PostgresException)exception).ConstraintName);
    }

    [Fact]
    public async Task DeletingAUserWhoActed_IsRestricted()
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);
        await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId));

        var exception = await Record.ExceptionAsync(() => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM users WHERE id = @id;", ("id", userId)));

        // RESTRICT is reported as 23001 by PostgreSQL 18 and 23503 by earlier versions.
        var postgres = Assert.IsType<PostgresException>(exception);
        Assert.Contains(postgres.SqlState, new[] { "23001", "23503" });
        Assert.Equal("fk_audit_events_users_actor_user_id", postgres.ConstraintName);
    }

    // ---- uniqueness ------------------------------------------------------

    [Fact]
    public async Task IdempotencyKey_IsGloballyUnique_WhenPresent()
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);

        await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(("idempotency_key", "PasswordChanged:op-1")));

        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(
            ("idempotency_key", "PasswordChanged:op-1"))), "23505", "ux_audit_events_idempotency_key");
        // Even for a different actor, entity and source: the index is global.
        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.UserRow(userId).With(
            ("idempotency_key", "PasswordChanged:op-1"), ("source", "EmployeeApp"), ("entity_id", Guid.CreateVersion7()))),
            "23505", "ux_audit_events_idempotency_key");
    }

    [Fact]
    public async Task IdempotencyKey_SeveralEventsOfOneOperation_DoNotCollide_WhenTheActionTypesDiffer()
    {
        await using var connection = await OpenAsync();
        var before = await CountAsync(connection);

        foreach (var action in new[] { "UserCreated", "RoleAssigned", "PayrollViewGranted" })
        {
            await AuditSql.InsertAsync(connection, AuditSql.SystemRow().With(
                ("action_type", action), ("idempotency_key", $"{action}:bootstrap:abc")));
        }

        Assert.Equal(before + 3, await CountAsync(connection));

        // Two events of the same action type under one operation need the sub-identifier.
        await AuditSql.FailsAsync(() => AuditSql.InsertAsync(connection, AuditSql.SystemRow().With(
            ("action_type", "RoleAssigned"), ("idempotency_key", "RoleAssigned:bootstrap:abc"))), "23505", "ux_audit_events_idempotency_key");
        await AuditSql.InsertAsync(connection, AuditSql.SystemRow().With(
            ("action_type", "RoleAssigned"), ("idempotency_key", "RoleAssigned:bootstrap:abc:assignment-2")));
    }

    [Fact]
    public async Task IdempotencyKey_ManyNullKeys_AreAllowed()
    {
        await using var connection = await OpenAsync();
        var userId = await InsertUserAsync(connection);

        var before = await PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM audit_events WHERE idempotency_key IS NULL;");

        for (var i = 0; i < 5; i++)
        {
            await AuditSql.InsertAsync(connection, AuditSql.UserRow(userId));
        }

        Assert.Equal(before + 5, await PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM audit_events WHERE idempotency_key IS NULL;"));
    }

    // ---- helpers ---------------------------------------------------------

    // Rows can never be deleted, so tests of one class share a growing table and count differences.
    private static Task<long> CountAsync(NpgsqlConnection connection) =>
        PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM audit_events;");

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
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
