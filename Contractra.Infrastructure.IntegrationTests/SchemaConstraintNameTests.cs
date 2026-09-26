using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

public class SchemaConstraintNameTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public SchemaConstraintNameTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("pk_parties")]
    [InlineData("ck_parties_status_valid_values")]
    [InlineData("pk_party_roles")]
    [InlineData("fk_party_roles_parties_party_id")]
    [InlineData("ck_party_roles_effective_to_after_from")]
    [InlineData("ex_party_roles_no_overlapping_period")]
    [InlineData("pk_party_aliases")]
    [InlineData("fk_party_aliases_parties_party_id")]
    [InlineData("pk_party_identifiers")]
    [InlineData("fk_party_identifiers_parties_party_id")]
    [InlineData("pk_roles")]
    [InlineData("ck_roles_status_valid_values")]
    [InlineData("pk_permissions")]
    [InlineData("ck_permissions_status_valid_values")]
    [InlineData("pk_role_permissions")]
    [InlineData("fk_role_permissions_roles_role_id")]
    [InlineData("fk_role_permissions_permissions_permission_id")]
    [InlineData("pk_users")]
    [InlineData("ck_users_status_valid_values")]
    [InlineData("pk_user_roles")]
    [InlineData("fk_user_roles_users_user_id")]
    [InlineData("fk_user_roles_roles_role_id")]
    [InlineData("ck_user_roles_ended_at_after_assigned_at")]
    [InlineData("ex_user_roles_no_overlapping_period")]
    [InlineData("pk_user_permissions")]
    [InlineData("fk_user_permissions_users_user_id")]
    [InlineData("fk_user_permissions_permissions_permission_id")]
    [InlineData("ck_user_permissions_ended_at_after_granted_at")]
    [InlineData("ex_user_permissions_no_overlapping_period")]
    [InlineData("pk_user_party_links")]
    [InlineData("fk_user_party_links_users_user_id")]
    [InlineData("fk_user_party_links_parties_party_id")]
    public async Task ExpectedConstraint_ExistsInPgConstraint(string constraintName)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM pg_constraint WHERE conname = @name;", connection);
        command.Parameters.AddWithValue("name", constraintName);

        var count = (long)(await command.ExecuteScalarAsync())!;
        Assert.True(count == 1, $"Expected exactly one constraint named '{constraintName}' in pg_constraint, found {count}.");
    }

    [Theory]
    [InlineData("ux_party_roles_party_id_role_code_effective_from")]
    [InlineData("ux_party_aliases_party_id_alias")]
    [InlineData("ix_party_identifiers_party_id")]
    [InlineData("ux_party_identifiers_current_scope")]
    [InlineData("ux_roles_code_lower")]
    [InlineData("ux_permissions_code_lower")]
    [InlineData("ux_role_permissions_role_id_permission_id")]
    [InlineData("ix_role_permissions_permission_id")]
    [InlineData("ux_users_login_identifier_lower")]
    [InlineData("ux_user_roles_user_id_role_id_active")]
    [InlineData("ix_user_roles_role_id")]
    [InlineData("ux_user_permissions_user_id_permission_id_active")]
    [InlineData("ix_user_permissions_permission_id")]
    [InlineData("ux_user_party_links_user_id")]
    [InlineData("ix_user_party_links_party_id")]
    public async Task ExpectedIndex_ExistsInPgIndexes(string indexName)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM pg_indexes WHERE indexname = @name;", connection);
        command.Parameters.AddWithValue("name", indexName);

        var count = (long)(await command.ExecuteScalarAsync())!;
        Assert.True(count == 1, $"Expected exactly one index named '{indexName}' in pg_indexes, found {count}.");
    }

    [Theory]
    [InlineData("ux_roles_code_lower", "roles")]
    [InlineData("ux_permissions_code_lower", "permissions")]
    public async Task CodeUniqueIndexes_AreUniqueOnLowerCode(string indexName, string tableName)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE indexname = @name AND tablename = @table;", connection);
        command.Parameters.AddWithValue("name", indexName);
        command.Parameters.AddWithValue("table", tableName);

        var definition = (string?)await command.ExecuteScalarAsync();
        Assert.NotNull(definition);
        Assert.Contains("CREATE UNIQUE INDEX", definition);
        Assert.Contains("lower(code)", definition);
    }

    [Fact]
    public async Task LoginIdentifierUniqueIndex_IsUniqueOnLowerLoginIdentifier_AndReplacesTheCaseSensitiveOne()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = current_schema() AND tablename = 'users' AND indexdef LIKE 'CREATE UNIQUE INDEX%' AND indexdef LIKE '%login_identifier%';",
            connection);

        var uniqueIndexes = new List<(string Name, string Definition)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                uniqueIndexes.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var index = Assert.Single(uniqueIndexes);
        Assert.Equal("ux_users_login_identifier_lower", index.Name);
        Assert.Contains("lower(login_identifier)", index.Definition);
    }

    [Fact]
    public async Task UserRoleActiveIndex_IsUniqueAndPartialOnOpenAssignments()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_user_roles_user_id_role_id_active' AND tablename = 'user_roles';",
            connection);

        var definition = (string?)await command.ExecuteScalarAsync();
        Assert.NotNull(definition);
        Assert.Contains("CREATE UNIQUE INDEX", definition);
        Assert.Contains("(user_id, role_id)", definition);
        Assert.Contains("WHERE (ended_at IS NULL)", definition);
    }

    [Fact]
    public async Task UserPermissionActiveIndex_IsUniqueAndPartialOnActiveGrants()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_user_permissions_user_id_permission_id_active' AND tablename = 'user_permissions';",
            connection);

        var definition = (string?)await command.ExecuteScalarAsync();
        Assert.NotNull(definition);
        Assert.Contains("CREATE UNIQUE INDEX", definition);
        Assert.Contains("(user_id, permission_id)", definition);
        Assert.Contains("WHERE (ended_at IS NULL)", definition);
    }

    [Fact]
    public async Task UserPartyLinks_AreUniqueOnUserOnly_NotOnParty()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT indexname, indexdef FROM pg_indexes WHERE tablename = 'user_party_links' AND indexname <> 'pk_user_party_links' ORDER BY indexname;",
            connection);

        var indexes = new Dictionary<string, string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                indexes[reader.GetString(0)] = reader.GetString(1);
            }
        }

        Assert.StartsWith("CREATE UNIQUE INDEX", indexes["ux_user_party_links_user_id"]);
        Assert.Contains("(user_id)", indexes["ux_user_party_links_user_id"]);
        Assert.StartsWith("CREATE INDEX", indexes["ix_user_party_links_party_id"]);
        Assert.DoesNotContain(indexes.Values, def => def.StartsWith("CREATE UNIQUE INDEX") && def.Contains("(party_id)"));
    }

    [Fact]
    public async Task UserPermissions_HaveNoPayrollViewTrigger_BecauseTheyAreTheSanctionedPath()
    {
        await using var connection = await OpenConnectionAsync();

        Assert.Equal(0, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid WHERE c.relname = 'user_permissions' AND NOT t.tgisinternal;"));
    }

    [Theory]
    [InlineData("fk_user_roles_users_user_id")]
    [InlineData("fk_user_roles_roles_role_id")]
    [InlineData("fk_user_permissions_users_user_id")]
    [InlineData("fk_user_permissions_permissions_permission_id")]
    [InlineData("fk_user_party_links_users_user_id")]
    [InlineData("fk_user_party_links_parties_party_id")]
    public async Task UserLinkForeignKeys_AreOnDeleteRestrict(string constraintName)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT confdeltype::text FROM pg_constraint WHERE conname = @name AND contype = 'f';", connection);
        command.Parameters.AddWithValue("name", constraintName);

        Assert.Equal("r", (string?)await command.ExecuteScalarAsync()); // r = RESTRICT
    }

    [Theory]
    [InlineData("fk_role_permissions_roles_role_id")]
    [InlineData("fk_role_permissions_permissions_permission_id")]
    public async Task RolePermissionForeignKeys_AreOnDeleteRestrict(string constraintName)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT confdeltype::text FROM pg_constraint WHERE conname = @name AND contype = 'f';", connection);
        command.Parameters.AddWithValue("name", constraintName);

        Assert.Equal("r", (string?)await command.ExecuteScalarAsync()); // r = RESTRICT
    }

    [Theory]
    [InlineData("trg_role_permissions_forbid_payroll_view", "role_permissions", "fn_role_permissions_forbid_payroll_view")]
    [InlineData("trg_permissions_forbid_granted_rename_to_payroll_view", "permissions", "fn_permissions_forbid_granted_rename_to_payroll_view")]
    public async Task PayrollViewGuardTriggers_ExistAndAreEnabled(string triggerName, string tableName, string functionName)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid
            JOIN pg_proc p ON p.oid = t.tgfoid
            WHERE t.tgname = @trigger
              AND c.relname = @table
              AND p.proname = @function
              AND t.tgenabled = 'O'
              AND NOT t.tgisinternal;
            """,
            connection);
        command.Parameters.AddWithValue("trigger", triggerName);
        command.Parameters.AddWithValue("table", tableName);
        command.Parameters.AddWithValue("function", functionName);

        var count = (long)(await command.ExecuteScalarAsync())!;
        Assert.True(count == 1, $"Expected enabled trigger '{triggerName}' on '{tableName}' calling '{functionName}', found {count}.");
    }

    [Fact]
    public async Task BtreeGistExtension_IsInstalled()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM pg_extension WHERE extname = 'btree_gist';", connection);

        var count = (long)(await command.ExecuteScalarAsync())!;
        Assert.Equal(1, count);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
