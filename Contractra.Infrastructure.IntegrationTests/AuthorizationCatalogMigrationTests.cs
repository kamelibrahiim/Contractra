using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Runs in its own ephemeral database (own fixture) because it migrates the
/// schema down and back up.
/// </summary>
public class AuthorizationCatalogMigrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string PreviousMigration = "20260922111924_InitialPartySlice";

    private readonly PostgresTestDatabaseFixture _fixture;

    public AuthorizationCatalogMigrationTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Down_RemovesEverySliceObject_KeepsEarlierObjects_AndUpReappliesCleanly()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);

        await using (var connection = await OpenConnectionAsync())
        {
            Assert.Equal(0, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_class WHERE relname IN ('roles', 'permissions', 'role_permissions', 'ux_roles_code_lower', 'ux_permissions_code_lower', 'ux_role_permissions_role_id_permission_id', 'ix_role_permissions_permission_id');"));
            Assert.Equal(0, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_proc WHERE proname IN ('fn_role_permissions_forbid_payroll_view', 'fn_permissions_forbid_granted_rename_to_payroll_view');"));
            Assert.Equal(0, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_trigger WHERE tgname IN ('trg_role_permissions_forbid_payroll_view', 'trg_permissions_forbid_granted_rename_to_payroll_view');"));

            // Objects owned by the earlier migration are untouched.
            Assert.Equal(4, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_class WHERE relname IN ('parties', 'party_roles', 'party_aliases', 'party_identifiers') AND relkind = 'r';"));
            Assert.Equal(1, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_extension WHERE extname = 'btree_gist';"));
        }

        await migrator.MigrateAsync();

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        await using (var connection = await OpenConnectionAsync())
        {
            Assert.Equal(2, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_trigger WHERE tgname IN ('trg_role_permissions_forbid_payroll_view', 'trg_permissions_forbid_granted_rename_to_payroll_view');"));
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
