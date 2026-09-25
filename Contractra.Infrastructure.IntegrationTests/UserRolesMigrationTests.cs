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
public class UserRolesMigrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string PreviousMigration = "20260924183838_MakeUserLoginIdentifierCaseInsensitive";

    private readonly PostgresTestDatabaseFixture _fixture;

    public UserRolesMigrationTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Down_RemovesOnlyUserRoles_KeepsUsersAndEarlierObjects_AndUpReappliesCleanly()
    {
        var user = new User(new LoginIdentifier("Migration.RoleHolder"), UserStatus.Active);
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
                "SELECT COUNT(*) FROM pg_class WHERE relname IN ('user_roles', 'ux_user_roles_user_id_role_id_active', 'ix_user_roles_role_id');"));
            Assert.Equal(0, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_constraint WHERE conname LIKE '%user_roles%';"));

            // Objects owned by earlier migrations are untouched.
            Assert.Equal(1, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ux_users_login_identifier_lower';"));
            Assert.Equal(1, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_extension WHERE extname = 'btree_gist';"));
            Assert.Equal(1, await PostgresAssert.CountAsync(connection, null,
                "SELECT COUNT(*) FROM users WHERE id = @id;", ("id", user.Id)));
        }

        await migrator.MigrateAsync();

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        await using (var connection = await OpenConnectionAsync())
        {
            Assert.Equal(1, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_constraint WHERE conname = 'ex_user_roles_no_overlapping_period';"));
            Assert.Equal(1, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ux_user_roles_user_id_role_id_active';"));
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
