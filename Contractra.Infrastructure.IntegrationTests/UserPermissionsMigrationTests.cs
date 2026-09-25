using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Runs in its own ephemeral database (own fixture) because it migrates the
/// schema down and back up.
/// </summary>
public class UserPermissionsMigrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string PreviousMigration = "20260925133208_AddUserRoles";

    private readonly PostgresTestDatabaseFixture _fixture;

    public UserPermissionsMigrationTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Down_RemovesOnlyUserPermissions_KeepsEarlierObjects_AndUpReappliesCleanly()
    {
        var user = new User(new LoginIdentifier("Migration.PermissionHolder"), UserStatus.Active);
        await using (var seedContext = _fixture.CreateDbContext())
        {
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);

        await using (var connection = await OpenConnectionAsync())
        {
            Assert.Equal(0, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_class WHERE relname IN ('user_permissions', 'ux_user_permissions_user_id_permission_id_active', 'ix_user_permissions_permission_id');"));
            Assert.Equal(0, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_constraint WHERE conname LIKE '%user_permissions%';"));

            // Objects owned by earlier migrations are untouched, including the
            // Payroll.View guard on role_permissions.
            Assert.Equal(1, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_constraint WHERE conname = 'ex_user_roles_no_overlapping_period';"));
            Assert.Equal(2, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_trigger WHERE tgname IN ('trg_role_permissions_forbid_payroll_view', 'trg_permissions_forbid_granted_rename_to_payroll_view');"));
            Assert.Equal(1, await PostgresAssert.CountAsync(connection, null,
                "SELECT COUNT(*) FROM users WHERE id = @id;", ("id", user.Id)));
        }

        await migrator.MigrateAsync();

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        await using (var connection = await OpenConnectionAsync())
        {
            Assert.Equal(1, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_constraint WHERE conname = 'ex_user_permissions_no_overlapping_period';"));
            Assert.Equal(1, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ux_user_permissions_user_id_permission_id_active';"));
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static Task<long> CountAsync(NpgsqlConnection connection, string sql) =>
        PostgresAssert.CountAsync(connection, null, sql);
}
