using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// AddUserCredentialState is additive and data-preserving (plan E16): rows that
/// existed before it are backfilled as accounts without a usable password, and
/// down-and-up leaves earlier data intact. Own database: migrates down and up.
/// </summary>
public class UserCredentialStateMigrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string PreviousMigration = "20260926091750_SeedAuthorizationCatalog";
    private const string ThisMigration = "20261014233000_AddUserCredentialState";

    private const string NewColumns =
        "'password_hash', 'must_change_password', 'temporary_password_expires_at', 'security_version', 'is_bootstrap_admin'";

    private readonly PostgresTestDatabaseFixture _fixture;

    public UserCredentialStateMigrationTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Up_BackfillsExistingRows_WithoutChangingThem_AndDownUpReappliesCleanly()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(0, await CountAsync(connection,
            $"SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'users' AND column_name IN ({NewColumns});"));

        var activeId = Guid.CreateVersion7();
        var disabledId = Guid.CreateVersion7();
        const string insert = "INSERT INTO users (id, login_identifier, status) VALUES (@id, @login, @status);";
        await PostgresAssert.ExecuteAsync(connection, null, insert, ("id", activeId), ("login", "PreCredential.Active"), ("status", "Active"));
        await PostgresAssert.ExecuteAsync(connection, null, insert, ("id", disabledId), ("login", "PreCredential.Disabled"), ("status", "Disabled"));

        await migrator.MigrateAsync(ThisMigration);

        foreach (var (id, status) in new[] { (activeId, "Active"), (disabledId, "Disabled") })
        {
            await using var command = new NpgsqlCommand(
                "SELECT login_identifier, status, password_hash, must_change_password, temporary_password_expires_at, security_version, is_bootstrap_admin FROM users WHERE id = @id;",
                connection);
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.StartsWith("PreCredential.", reader.GetString(0));
            Assert.Equal(status, reader.GetString(1));
            Assert.Equal("!", reader.GetString(2));
            Assert.True(reader.GetBoolean(3));
            Assert.True(reader.IsDBNull(4));
            Assert.Equal(1, reader.GetInt32(5));
            Assert.False(reader.GetBoolean(6));
        }

        // Backfilled rows load through the Domain as accounts with no usable password.
        await using (var loadContext = _fixture.CreateDbContext())
        {
            var loaded = await loadContext.Users.SingleAsync(u => u.Id == activeId);
            Assert.False(loaded.HasUsablePassword);
            Assert.True(loaded.MustChangePassword);
        }

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(0, await CountAsync(connection,
            $"SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'users' AND column_name IN ({NewColumns});"));
        Assert.Equal(0, await CountAsync(connection,
            "SELECT COUNT(*) FROM pg_proc WHERE proname = 'fn_users_forbid_bootstrap_flag_change';"));
        Assert.Equal(2, await CountAsync(connection, "SELECT COUNT(*) FROM users WHERE login_identifier LIKE 'PreCredential.%';"));

        await migrator.MigrateAsync();
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        Assert.Equal(5, await CountAsync(connection,
            $"SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'users' AND column_name IN ({NewColumns});"));
        Assert.Equal(1, await CountAsync(connection,
            "SELECT COUNT(*) FROM pg_trigger WHERE tgname = 'trg_users_forbid_bootstrap_flag_change';"));
    }

    private static Task<long> CountAsync(NpgsqlConnection connection, string sql) =>
        PostgresAssert.CountAsync(connection, null, sql);
}
