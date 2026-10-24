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
public class UserLoginIdentifierMigrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string PreviousMigration = "20260924112106_AddUsers";

    private readonly PostgresTestDatabaseFixture _fixture;

    public UserLoginIdentifierMigrationTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Down_RestoresOnlyTheCaseSensitiveIndex_KeepsUserData_AndUpReappliesCleanly()
    {
        var user = TestUsers.NewUser("Migration.Kamel", UserStatus.Active);
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
                "SELECT COUNT(*) FROM pg_class WHERE relname = 'ux_users_login_identifier_lower';"));
            Assert.Equal(1, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ux_users_login_identifier' AND indexdef LIKE 'CREATE UNIQUE INDEX%';"));
            Assert.Equal("Migration.Kamel", await LoginIdentifierOfAsync(connection, user.Id));
        }

        await migrator.MigrateAsync();

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        await using (var connection = await OpenConnectionAsync())
        {
            Assert.Equal(1, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ux_users_login_identifier_lower';"));
            Assert.Equal(0, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_class WHERE relname = 'ux_users_login_identifier';"));
            Assert.Equal("Migration.Kamel", await LoginIdentifierOfAsync(connection, user.Id));
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    internal static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    internal static async Task<string?> LoginIdentifierOfAsync(NpgsqlConnection connection, Guid id)
    {
        await using var command = new NpgsqlCommand("SELECT login_identifier FROM users WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", id);
        return (string?)await command.ExecuteScalarAsync();
    }
}

/// <summary>
/// Own ephemeral database: it deliberately leaves the schema at AddUsers with
/// case-only duplicate rows, which no other test may share.
/// </summary>
public class UserLoginIdentifierMigrationDuplicateTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string PreviousMigration = "20260924112106_AddUsers";

    private readonly PostgresTestDatabaseFixture _fixture;

    public UserLoginIdentifierMigrationDuplicateTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Up_FailsAndRollsBack_WhenCaseOnlyDuplicatesExist_WithoutChangingRows()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var upperId = Guid.CreateVersion7();
        var lowerId = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        const string insert = "INSERT INTO users (id, login_identifier, status) VALUES (@id, @login_identifier, 'Active');";
        await PostgresAssert.ExecuteAsync(connection, null, insert, ("id", upperId), ("login_identifier", "Dup.Kamel"));
        await PostgresAssert.ExecuteAsync(connection, null, insert, ("id", lowerId), ("login_identifier", "dup.kamel"));

        await PostgresAssert.FailsWithAsync(() => migrator.MigrateAsync(), "23505");

        Assert.Equal(0, await UserLoginIdentifierMigrationTests.CountAsync(connection,
            "SELECT COUNT(*) FROM pg_class WHERE relname = 'ux_users_login_identifier_lower';"));
        Assert.Equal(1, await UserLoginIdentifierMigrationTests.CountAsync(connection,
            "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ux_users_login_identifier';"));
        Assert.Equal("Dup.Kamel", await UserLoginIdentifierMigrationTests.LoginIdentifierOfAsync(connection, upperId));
        Assert.Equal("dup.kamel", await UserLoginIdentifierMigrationTests.LoginIdentifierOfAsync(connection, lowerId));
        Assert.Contains("20260924183838_MakeUserLoginIdentifierCaseInsensitive", await dbContext.Database.GetPendingMigrationsAsync());
    }
}
