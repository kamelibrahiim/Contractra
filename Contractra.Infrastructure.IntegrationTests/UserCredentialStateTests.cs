using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Credential and security-state columns on users (Backend Sprint 2, US-BE-007;
/// auth contract v1.5 §3.5, §10, §11). Raw-SQL checks prove the database holds
/// the rules on its own, independently of the Domain.
/// </summary>
public class UserCredentialStateTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgresTestDatabaseFixture _fixture;

    public UserCredentialStateTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CredentialState_RoundTripsThroughEfCore()
    {
        var expiresAt = T0.AddHours(72);
        var user = new User(new LoginIdentifier($"Cred.{Guid.NewGuid():N}"), UserStatus.Active, TestUsers.Hash, mustChangePassword: true, temporaryPasswordExpiresAt: expiresAt);
        await using (var dbContext = _fixture.CreateDbContext())
        {
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var stored = await verifyContext.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(TestUsers.Hash, stored.PasswordHash);
        Assert.True(stored.MustChangePassword);
        Assert.Equal(expiresAt, stored.TemporaryPasswordExpiresAt);
        Assert.Equal(1, stored.SecurityVersion);
        Assert.False(stored.IsBootstrapAdmin);
    }

    [Fact]
    public async Task SecurityVersion_IsPersistedWithEveryChange()
    {
        var user = TestUsers.NewUser($"Cred.{Guid.NewGuid():N}");
        await using (var dbContext = _fixture.CreateDbContext())
        {
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var tracked = await dbContext.Users.SingleAsync(u => u.Id == user.Id);
            tracked.Disable();
            tracked.Reactivate();
            tracked.BeginPasswordReset();
            await dbContext.SaveChangesAsync();
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var stored = await verifyContext.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(4, stored.SecurityVersion);
        Assert.Equal(User.UnusablePasswordHash, stored.PasswordHash);
        Assert.False(stored.HasUsablePassword);
    }

    [Fact]
    public async Task ConcurrentChanges_ToTheSameUser_CannotBothBeSaved()
    {
        // security_version is the concurrency token: two writers that loaded the
        // same version cannot both commit, so a change is never hidden behind a
        // version number another change already used (plan E7).
        var user = TestUsers.NewUser($"Cred.{Guid.NewGuid():N}");
        await using (var dbContext = _fixture.CreateDbContext())
        {
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        await using var first = _fixture.CreateDbContext();
        await using var second = _fixture.CreateDbContext();
        var a = await first.Users.SingleAsync(u => u.Id == user.Id);
        var b = await second.Users.SingleAsync(u => u.Id == user.Id);

        a.Disable();
        await first.SaveChangesAsync();

        b.BeginPasswordReset();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var verifyContext = _fixture.CreateDbContext();
        var stored = await verifyContext.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(UserStatus.Disabled, stored.Status);
        Assert.Equal(TestUsers.Hash, stored.PasswordHash);
        Assert.Equal(2, stored.SecurityVersion);
    }

    [Fact]
    public async Task EmptyPasswordHash_IsRejected()
    {
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, passwordHash: "", mustChange: false, securityVersion: 1),
            "23514",
            "ck_users_password_hash_not_empty");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SecurityVersionBelowOne_IsRejected(int securityVersion)
    {
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, TestUsers.Hash, mustChange: false, securityVersion),
            "23514",
            "ck_users_security_version_positive");
    }

    [Fact]
    public async Task TemporaryPasswordExpiry_RequiresTheForcedChange()
    {
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, TestUsers.Hash, mustChange: false, securityVersion: 1, temporaryPasswordExpiresAt: T0),
            "23514",
            "ck_users_temporary_password_requires_change");
    }

    [Fact]
    public async Task IsBootstrapAdmin_DefaultsToFalse()
    {
        await using var connection = await OpenConnectionAsync();
        var id = await InsertAsync(connection, TestUsers.Hash, mustChange: false, securityVersion: 1);

        await using var command = new NpgsqlCommand("SELECT is_bootstrap_admin FROM users WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", id);
        Assert.False((bool)(await command.ExecuteScalarAsync())!);
    }

    [Theory]
    [InlineData("password_hash")]
    [InlineData("must_change_password")]
    [InlineData("security_version")]
    public async Task CredentialColumns_HaveNoDatabaseDefault(string column)
    {
        // Backfill defaults were dropped after the migration: every new row must
        // state its credential state explicitly.
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT column_default FROM information_schema.columns WHERE table_name = 'users' AND column_name = @column;", connection);
        command.Parameters.AddWithValue("column", column);

        Assert.Equal(DBNull.Value, await command.ExecuteScalarAsync());
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<Guid> InsertAsync(
        NpgsqlConnection connection,
        string passwordHash,
        bool mustChange,
        int securityVersion,
        DateTimeOffset? temporaryPasswordExpiresAt = null)
    {
        var id = Guid.CreateVersion7();
        await PostgresAssert.ExecuteAsync(
            connection,
            null,
            "INSERT INTO users (id, login_identifier, status, password_hash, must_change_password, security_version, temporary_password_expires_at) " +
            "VALUES (@id, @login, 'Active', @hash, @must_change, @sv, @expires);",
            ("id", id),
            ("login", $"Raw.{Guid.NewGuid():N}"),
            ("hash", passwordHash),
            ("must_change", mustChange),
            ("sv", securityVersion),
            ("expires", (object?)temporaryPasswordExpiresAt ?? DBNull.Value));
        return id;
    }
}
