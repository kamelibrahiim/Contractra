using Contractra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Creates a uniquely named, throwaway PostgreSQL database for a single test class,
/// applies the Contractra migration to it, and - only if explicitly opted in - drops
/// it on teardown.
///
/// Safety rules (approved for this slice):
/// - Reads ONLY the CONTRACTRA_TEST_POSTGRES environment variable - never falls back
///   to ConnectionStrings:DefaultConnection or any other source.
/// - Refuses to run if that variable is missing, or if it points at a database named
///   "contractra_dev".
/// - Every created database is named "contractra_test_&lt;guid&gt;".
/// - By default, the created database is PRESERVED after the test run (teardown does
///   not drop it), so a developer can inspect it afterward; this is deliberate, not a
///   leak. Set CONTRACTRA_TEST_ALLOW_DATABASE_DROP=true to opt into automatic cleanup.
/// - Even when that opt-in is set, DROP DATABASE is only ever issued after verifying
///   the target name starts with "contractra_test_" - and only this instance's own
///   database, which it created itself moments earlier and holds in a private field;
///   it never accepts a database name from any external source.
/// - Never logs or throws the connection string or password.
/// </summary>
public sealed class PostgresTestDatabaseFixture : IAsyncLifetime
{
    private const string TestDatabasePrefix = "contractra_test_";
    private const string ForbiddenDevDatabaseName = "contractra_dev";
    private const string EnvironmentVariableName = "CONTRACTRA_TEST_POSTGRES";
    private const string AllowDropEnvironmentVariableName = "CONTRACTRA_TEST_ALLOW_DATABASE_DROP";

    private string? _adminConnectionString;
    private string? _testConnectionString;

    public string DatabaseName { get; private set; } = string.Empty;

    internal string ConnectionString =>
        _testConnectionString ?? throw new InvalidOperationException("The test database has not been initialized yet.");

    public async Task InitializeAsync()
    {
        var serverConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariableName);

        if (string.IsNullOrWhiteSpace(serverConnectionString))
        {
            throw new InvalidOperationException(
                $"PostgreSQL integration tests require the '{EnvironmentVariableName}' environment variable " +
                "to be set to a PostgreSQL SERVER connection string (not the Contractra development database). " +
                "ConnectionStrings:DefaultConnection is never used for tests.");
        }

        var serverBuilder = new NpgsqlConnectionStringBuilder(serverConnectionString);

        if (string.Equals(serverBuilder.Database, ForbiddenDevDatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{EnvironmentVariableName}' must not target the '{ForbiddenDevDatabaseName}' database. " +
                "Point it at the server's maintenance database (e.g. 'postgres') instead.");
        }

        // Connect to an admin/maintenance database to create and drop the ephemeral test database.
        var adminBuilder = new NpgsqlConnectionStringBuilder(serverConnectionString)
        {
            Database = string.IsNullOrWhiteSpace(serverBuilder.Database) ? "postgres" : serverBuilder.Database
        };
        _adminConnectionString = adminBuilder.ConnectionString;

        DatabaseName = $"{TestDatabasePrefix}{Guid.NewGuid():N}";

        await using (var adminConnection = new NpgsqlConnection(_adminConnectionString))
        {
            await adminConnection.OpenAsync();
            await using var createCommand = new NpgsqlCommand(
                $"CREATE DATABASE \"{DatabaseName}\";", adminConnection);
            await createCommand.ExecuteNonQueryAsync();
        }

        var testBuilder = new NpgsqlConnectionStringBuilder(serverConnectionString)
        {
            Database = DatabaseName
        };
        _testConnectionString = testBuilder.ConnectionString;

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public ContractraDbContext CreateDbContext()
    {
        if (_testConnectionString is null)
        {
            throw new InvalidOperationException("The test database has not been initialized yet.");
        }

        var options = new DbContextOptionsBuilder<ContractraDbContext>()
            .UseNpgsql(_testConnectionString)
            .Options;

        return new ContractraDbContext(options);
    }

    public async Task DisposeAsync()
    {
        // Clears only this fixture's own pool - the one keyed by its own unique
        // _testConnectionString (Database=contractra_test_<guid>), never the
        // process-wide ClearAllPools(). Npgsql pools are looked up by the exact
        // connection string (PoolManager), so this cannot touch another
        // fixture's pool, which is keyed by its own, different database name.
        // Runs before the preservation early-return below, and before the
        // top guard's own early return, so it still happens even when
        // InitializeAsync got far enough to build _testConnectionString but
        // then failed later (e.g. during MigrateAsync) - idle connections this
        // fixture opened against its own database are released promptly
        // instead of waiting out Npgsql's default 300-second
        // ConnectionIdleLifetime. If _testConnectionString was never set (a
        // failure before that point), there is no pool for this fixture's test
        // database to clear, so this is skipped.
        if (_testConnectionString is not null)
        {
            using var testConnection = new NpgsqlConnection(_testConnectionString);
            NpgsqlConnection.ClearPool(testConnection);
        }

        if (_adminConnectionString is null || string.IsNullOrEmpty(DatabaseName))
        {
            return;
        }

        // Default-preserve: automatic deletion is an explicit opt-in, not the
        // default, so a test database survives for inspection unless a developer
        // or CI job asks for cleanup. This also covers the case where InitializeAsync
        // created the database but a later step (e.g. migration) then threw - this
        // method still runs, and still preserves that database by default.
        if (!string.Equals(
            Environment.GetEnvironmentVariable(AllowDropEnvironmentVariableName),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Safety guard: never drop anything that doesn't look like one of our
        // ephemeral test databases, even if something above went wrong.
        if (!DatabaseName.StartsWith(TestDatabasePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to drop database '{DatabaseName}': it does not start with '{TestDatabasePrefix}'.");
        }

        try
        {
            await using var adminConnection = new NpgsqlConnection(_adminConnectionString);
            await adminConnection.OpenAsync();
            await using var dropCommand = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE);", adminConnection);
            await dropCommand.ExecuteNonQueryAsync();
        }
        catch
        {
            // Cleanup must not mask the original test failure; a leftover
            // contractra_test_* database is safe to remove manually later.
        }
    }
}
