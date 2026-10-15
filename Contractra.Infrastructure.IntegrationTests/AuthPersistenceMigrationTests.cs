using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Each Slice 2B migration (ADR 0005 E17) migrates down and up on its own
/// ephemeral database: its table and constraints appear and disappear, and
/// users and catalog data are untouched. Down is a development/test tool
/// (plan E16): it drops the table with its rows.
/// </summary>
public abstract class AuthPersistenceMigrationTestBase
{
    internal const string SliceBase = "MakeUserPartyLinkOnePerParty";

    private readonly PostgresTestDatabaseFixture _fixture;

    protected AuthPersistenceMigrationTestBase(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    protected async Task AssertDownAndUpAsync(string previousMigration, string thisMigration, string table, int constraintCount)
    {
        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();
        var previous = FullName(dbContext, previousMigration);
        var current = FullName(dbContext, thisMigration);

        await migrator.MigrateAsync(previous);
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.False(await TableExistsAsync(connection, table));

        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Users.Add(TestUsers.NewUser($"Migration.{Guid.NewGuid():N}"));
            await seed.SaveChangesAsync();
        }

        var before = await DataFingerprintAsync(connection);

        await migrator.MigrateAsync(current);
        Assert.True(await TableExistsAsync(connection, table));
        Assert.Equal(constraintCount, await ConstraintCountAsync(connection, table));
        Assert.Equal(before, await DataFingerprintAsync(connection));

        await migrator.MigrateAsync(previous);
        Assert.False(await TableExistsAsync(connection, table));
        Assert.Equal(0, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM pg_constraint WHERE conname LIKE '%' || @table || '%' AND contype <> 'n';", ("table", table)));
        Assert.Equal(before, await DataFingerprintAsync(connection));

        await migrator.MigrateAsync();
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        Assert.True(await TableExistsAsync(connection, table));
        Assert.Equal(before, await DataFingerprintAsync(connection));
    }

    internal static string FullName(DbContext dbContext, string migrationName) =>
        dbContext.GetService<IMigrationsAssembly>().Migrations.Keys.Single(id => id.EndsWith("_" + migrationName, StringComparison.Ordinal));

    internal static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string table) =>
        await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = @table;",
            ("table", table)) == 1;

    // NOT NULL constraints are excluded: PostgreSQL 18 records them in
    // pg_constraint (contype 'n'), PostgreSQL 16 does not.
    internal static Task<long> ConstraintCountAsync(NpgsqlConnection connection, string table) =>
        PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM pg_constraint WHERE conrelid = to_regclass(@table) AND contype <> 'n';", ("table", table));

    /// <summary>Row counts of the pre-existing identity and catalog tables.</summary>
    internal static async Task<string> DataFingerprintAsync(NpgsqlConnection connection)
    {
        var counts = new List<long>();
        foreach (var table in new[] { "users", "user_roles", "user_permissions", "user_party_links", "roles", "permissions", "role_permissions", "parties" })
        {
            counts.Add(await PostgresAssert.CountAsync(connection, null, $"SELECT COUNT(*) FROM {table};"));
        }

        return string.Join(",", counts);
    }
}

public class AddAuthSessionsMigrationTests : AuthPersistenceMigrationTestBase, IClassFixture<PostgresTestDatabaseFixture>
{
    public AddAuthSessionsMigrationTests(PostgresTestDatabaseFixture fixture)
        : base(fixture)
    {
    }

    // pk, fk, seven checks
    [Fact]
    public Task DownAndUp() => AssertDownAndUpAsync(SliceBase, "AddAuthSessions", "auth_sessions", 9);
}

public class AddRefreshTokensMigrationTests : AuthPersistenceMigrationTestBase, IClassFixture<PostgresTestDatabaseFixture>
{
    public AddRefreshTokensMigrationTests(PostgresTestDatabaseFixture fixture)
        : base(fixture)
    {
    }

    // pk, fk, two checks, exclusion
    [Fact]
    public Task DownAndUp() => AssertDownAndUpAsync("AddAuthSessions", "AddRefreshTokens", "refresh_tokens", 5);
}

public class AddPasswordResetTicketsMigrationTests : AuthPersistenceMigrationTestBase, IClassFixture<PostgresTestDatabaseFixture>
{
    public AddPasswordResetTicketsMigrationTests(PostgresTestDatabaseFixture fixture)
        : base(fixture)
    {
    }

    // pk, two fks, seven checks, exclusion
    [Fact]
    public Task DownAndUp() => AssertDownAndUpAsync("AddRefreshTokens", "AddPasswordResetTickets", "password_reset_tickets", 11);
}

public class AddAuthThrottleCountersMigrationTests : AuthPersistenceMigrationTestBase, IClassFixture<PostgresTestDatabaseFixture>
{
    public AddAuthThrottleCountersMigrationTests(PostgresTestDatabaseFixture fixture)
        : base(fixture)
    {
    }

    // pk, four checks
    [Fact]
    public Task DownAndUp() => AssertDownAndUpAsync("AddPasswordResetTickets", "AddAuthThrottleCounters", "auth_throttle_counters", 5);
}

/// <summary>
/// The whole slice rolls back to MakeUserPartyLinkOnePerParty in one step -
/// dropping the four tables and their rows (development/test only, E16) -
/// while users, roles, permissions and party data survive, then re-applies.
/// </summary>
public class AuthPersistenceSliceMigrationTests : AuthPersistenceMigrationTestBase, IClassFixture<PostgresTestDatabaseFixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgresTestDatabaseFixture _fixture;

    public AuthPersistenceSliceMigrationTests(PostgresTestDatabaseFixture fixture)
        : base(fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WholeSlice_RollsBackAndForward_LeavingEarlierDataIntact()
    {
        var user = TestUsers.NewUser($"Slice2B.{Guid.NewGuid():N}");
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Users.Add(user);
            seed.AuthSessions.Add(AuthSession.Start(user.Id, ClientApp.Employee, ClientPlatform.Mobile, AuthPersistenceSchemaTests.Hash(1), T0, T0.AddDays(7)));
            seed.PasswordResetTickets.Add(PasswordResetTicket.Issue(user.Id, AuthPersistenceSchemaTests.Hash(2), T0, T0.AddHours(24), null, "operator"));
            await seed.SaveChangesAsync();
        }

        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var before = await DataFingerprintAsync(connection);

        await migrator.MigrateAsync(FullName(dbContext, SliceBase));
        foreach (var table in new[] { "auth_sessions", "refresh_tokens", "password_reset_tickets", "auth_throttle_counters" })
        {
            Assert.False(await TableExistsAsync(connection, table));
        }

        Assert.Equal(before, await DataFingerprintAsync(connection));

        await migrator.MigrateAsync();
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        Assert.Equal(before, await DataFingerprintAsync(connection));
        Assert.Equal(0, await PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM auth_sessions;"));
    }
}
