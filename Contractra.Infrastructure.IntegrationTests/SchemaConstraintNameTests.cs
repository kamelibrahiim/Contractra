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
    public async Task ExpectedIndex_ExistsInPgIndexes(string indexName)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM pg_indexes WHERE indexname = @name;", connection);
        command.Parameters.AddWithValue("name", indexName);

        var count = (long)(await command.ExecuteScalarAsync())!;
        Assert.True(count == 1, $"Expected exactly one index named '{indexName}' in pg_indexes, found {count}.");
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
