using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Database guards for the bootstrap admin (auth contract v1.5 §11; owner
/// decisions R19, R20; plan E7). Own database: at most one bootstrap row may
/// exist, so these tests must not share it with other classes.
/// </summary>
public class BootstrapAdminPersistenceTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string FlagGuard = "trg_users_forbid_bootstrap_flag_change";

    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgresTestDatabaseFixture _fixture;

    public BootstrapAdminPersistenceTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BootstrapAdmin_CreatedWithTheSeededCatalog_RoundTripsThroughEfCore()
    {
        var userId = await EnsureBootstrapAdminAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var stored = await verifyContext.Users
            .Include(u => u.Roles)
            .Include(u => u.Permissions)
            .SingleAsync(u => u.Id == userId);
        Assert.True(stored.IsBootstrapAdmin);
        Assert.False(stored.MustChangePassword);
        Assert.Single(stored.Roles);
        Assert.Single(stored.Permissions);
        Assert.Equal(3, stored.SecurityVersion);
    }

    [Fact]
    public async Task ASecondBootstrapAdmin_IsRejectedByThePartialUniqueIndex()
    {
        await EnsureBootstrapAdminAsync();
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => InsertRawAsync(connection, isBootstrapAdmin: true, mustChange: false),
            "23505",
            "ux_users_is_bootstrap_admin");
    }

    [Fact]
    public async Task ManyOrdinaryUsers_AreAllowed()
    {
        await using var connection = await OpenConnectionAsync();

        await InsertRawAsync(connection, isBootstrapAdmin: false, mustChange: false);
        await InsertRawAsync(connection, isBootstrapAdmin: false, mustChange: false);
    }

    [Fact]
    public async Task TheBootstrapAdmin_CanNeverBeForcedToChangeItsPassword()
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // Inside a rolled-back transaction so the single permitted bootstrap row
        // is never consumed by this test: the CHECK fires before the index.
        await PostgresAssert.FailsWithAsync(
            () => InsertRawAsync(connection, isBootstrapAdmin: true, mustChange: true, transaction),
            "23514",
            "ck_users_bootstrap_admin_never_must_change");
    }

    [Fact]
    public async Task TheBootstrapFlag_CannotBeCleared()
    {
        var userId = await EnsureBootstrapAdminAsync();
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(connection, null,
                "UPDATE users SET is_bootstrap_admin = false WHERE id = @id;", ("id", userId)),
            "23514",
            FlagGuard);
    }

    [Fact]
    public async Task TheBootstrapFlag_CannotBeGrantedToAnExistingUser()
    {
        await using var connection = await OpenConnectionAsync();
        var userId = await InsertRawAsync(connection, isBootstrapAdmin: false, mustChange: false);

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(connection, null,
                "UPDATE users SET is_bootstrap_admin = true WHERE id = @id;", ("id", userId)),
            "23514",
            FlagGuard);
    }

    [Fact]
    public async Task OtherColumnsOfTheBootstrapAdmin_RemainUpdatable()
    {
        // The guard is about the flag only: the bootstrap admin may still change
        // its own password (R26).
        var userId = await EnsureBootstrapAdminAsync();
        await using var connection = await OpenConnectionAsync();

        Assert.Equal(1, await PostgresAssert.ExecuteAsync(connection, null,
            "UPDATE users SET password_hash = @hash, security_version = security_version + 1 WHERE id = @id;",
            ("hash", "AQAAAAIAAYagAAAAENewBootstrapHash=="), ("id", userId)));
    }

    private async Task<Guid> EnsureBootstrapAdminAsync()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var existing = await dbContext.Users.SingleOrDefaultAsync(u => u.IsBootstrapAdmin);
        if (existing is not null)
        {
            return existing.Id;
        }

        var roles = await dbContext.Roles.ToListAsync();
        var permissions = await dbContext.Permissions.ToListAsync();
        var admin = roles.Single(r => r.Code.Value == "Admin");
        var payrollView = permissions.Single(p => p.Code.IsPayrollView);

        var user = User.CreateBootstrapAdmin(new LoginIdentifier("bootstrap.admin"), TestUsers.Hash, admin, payrollView, T0, T0);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<Guid> InsertRawAsync(
        NpgsqlConnection connection, bool isBootstrapAdmin, bool mustChange, NpgsqlTransaction? transaction = null)
    {
        var id = Guid.CreateVersion7();
        await PostgresAssert.ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO users (id, login_identifier, status, password_hash, must_change_password, security_version, is_bootstrap_admin, temporary_password_expires_at) " +
            "VALUES (@id, @login, 'Active', @hash, @must_change, 1, @bootstrap, @expires);",
            ("id", id),
            ("login", $"Raw.{Guid.NewGuid():N}"),
            ("hash", TestUsers.Hash),
            ("must_change", mustChange),
            ("bootstrap", isBootstrapAdmin),
            ("expires", mustChange ? T0.AddDays(3) : (object)DBNull.Value));
        return id;
    }
}
