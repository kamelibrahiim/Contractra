using Contractra.Infrastructure.Persistence.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// The AddAuditEvents migration (US-BE-009; repository ADR 0006 E39, E40, §7.1, §9):
/// it sits right after AddAuthThrottleCounters, migrates down and up leaving
/// users and catalog data intact, and revokes UPDATE, DELETE, TRUNCATE and TRIGGER from
/// every non-owner grantee — including PUBLIC and privileges granted automatically by
/// ALTER DEFAULT PRIVILEGES — without naming any role. Own database: migrates down.
/// </summary>
public class AddAuditEventsMigrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string PreviousMigration = "AddAuthThrottleCounters";
    private const string ThisMigration = "AddAuditEvents";

    private readonly PostgresTestDatabaseFixture _fixture;

    public AddAuditEventsMigrationTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migration_IsTheNewestOne_RightAfterTheThrottleCounters()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var ids = dbContext.GetService<IMigrationsAssembly>().Migrations.Keys.Order(StringComparer.Ordinal).ToArray();

        Assert.EndsWith("_" + ThisMigration, ids[^1]);
        Assert.EndsWith("_" + PreviousMigration, ids[^2]);
    }

    [Fact]
    public async Task DownAndUp_AddsAndRemovesTheTableFunctionAndTriggers_LeavingOtherDataIntact()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();
        var previous = AuthPersistenceMigrationTestBase.FullName(dbContext, PreviousMigration);
        var current = AuthPersistenceMigrationTestBase.FullName(dbContext, ThisMigration);
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await migrator.MigrateAsync(previous);
        Assert.False(await AuthPersistenceMigrationTestBase.TableExistsAsync(connection, "audit_events"));
        Assert.Equal(0, await ObjectCountAsync(connection));

        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Users.Add(TestUsers.NewUser($"Migration.{Guid.NewGuid():N}"));
            await seed.SaveChangesAsync();
        }

        var before = await AuthPersistenceMigrationTestBase.DataFingerprintAsync(connection);

        await migrator.MigrateAsync(current);
        Assert.True(await AuthPersistenceMigrationTestBase.TableExistsAsync(connection, "audit_events"));
        Assert.Equal(25, await AuthPersistenceMigrationTestBase.ConstraintCountAsync(connection, "audit_events"));
        Assert.Equal(3, await ObjectCountAsync(connection));
        Assert.Equal(before, await AuthPersistenceMigrationTestBase.DataFingerprintAsync(connection));

        await migrator.MigrateAsync(previous);
        Assert.False(await AuthPersistenceMigrationTestBase.TableExistsAsync(connection, "audit_events"));
        Assert.Equal(0, await ObjectCountAsync(connection));
        Assert.Equal(before, await AuthPersistenceMigrationTestBase.DataFingerprintAsync(connection));

        await migrator.MigrateAsync();
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        Assert.True(await AuthPersistenceMigrationTestBase.TableExistsAsync(connection, "audit_events"));
        Assert.Equal(before, await AuthPersistenceMigrationTestBase.DataFingerprintAsync(connection));
    }

    [Fact]
    public async Task Up_RevokesTheForbiddenPrivileges_FromPublicAndFromDefaultPrivilegeGrants()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();
        var previous = AuthPersistenceMigrationTestBase.FullName(dbContext, PreviousMigration);
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await migrator.MigrateAsync(previous);
        // PUBLIC is not a role, so this leaves nothing behind outside the ephemeral database.
        await PostgresAssert.ExecuteAsync(connection, null, "ALTER DEFAULT PRIVILEGES GRANT ALL ON TABLES TO PUBLIC;");
        try
        {
            await migrator.MigrateAsync();

            var granted = await PublicPrivilegesAsync(connection);
            Assert.DoesNotContain("UPDATE", granted);
            Assert.DoesNotContain("DELETE", granted);
            Assert.DoesNotContain("TRUNCATE", granted);
            Assert.DoesNotContain("TRIGGER", granted);
            // Only the four privileges are revoked: the rest of the grant is untouched.
            Assert.Contains("SELECT", granted);
            Assert.Contains("INSERT", granted);

            // Control: a table created in this same run, after the default-privilege
            // grant, proves the grant genuinely took effect and was not a no-op - the
            // revoke above is therefore undoing a real grant, not asserting on an
            // already-empty ACL. (users predates this test's ALTER DEFAULT PRIVILEGES
            // by a whole migration run, in PostgresTestDatabaseFixture.InitializeAsync,
            // so it can never carry a default-privilege grant: ALTER DEFAULT PRIVILEGES
            // only governs objects created afterward, never retroactively.)
            await PostgresAssert.ExecuteAsync(connection, null, "CREATE TABLE zzz_default_privilege_control(id int);");
            Assert.Contains("UPDATE", await PublicPrivilegesAsync(connection, "zzz_default_privilege_control"));
        }
        finally
        {
            await PostgresAssert.ExecuteAsync(connection, null, "ALTER DEFAULT PRIVILEGES REVOKE ALL ON TABLES FROM PUBLIC;");
        }
    }

    [Fact]
    public async Task Up_WithNoGrantsAtAll_RevokesNothingAndFailsNowhere()
    {
        // The usual case: no default privileges, an empty ACL (relacl is NULL).
        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(AuthPersistenceMigrationTestBase.FullName(dbContext, PreviousMigration));

        await migrator.MigrateAsync();

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.Empty(await PublicPrivilegesAsync(connection));
    }

    /// <summary>The function plus the two triggers.</summary>
    private static Task<long> ObjectCountAsync(NpgsqlConnection connection) =>
        PostgresAssert.CountAsync(connection, null,
            "SELECT (SELECT COUNT(*) FROM pg_proc WHERE proname = 'fn_audit_append_only') " +
            "+ (SELECT COUNT(*) FROM pg_trigger WHERE tgname IN ('trg_audit_events_forbid_update_delete', 'trg_audit_events_forbid_truncate'));");

    private static async Task<List<string>> PublicPrivilegesAsync(NpgsqlConnection connection, string table = "audit_events")
    {
        await using var command = new NpgsqlCommand(
            "SELECT acl.privilege_type FROM pg_class c CROSS JOIN LATERAL aclexplode(c.relacl) AS acl " +
            "WHERE c.oid = to_regclass(@table) AND acl.grantee = 0;", connection);
        command.Parameters.AddWithValue("table", table);

        var privileges = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            privileges.Add(reader.GetString(0));
        }

        return privileges;
    }
}

/// <summary>
/// Rolling back AddAuditEvents refuses to drop a non-empty audit table, so a rollback
/// cannot silently destroy audit history (plan E16; ADR 0006 E40). Own database: it keeps
/// an audit row that can never be deleted.
/// </summary>
public class AddAuditEventsDownGuardTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public AddAuditEventsDownGuardTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Down_Refuses_WhenTheTableHoldsRows_AndNothingIsLost()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var row = AuditSql.SystemRow();
        await AuditSql.InsertAsync(connection, row);

        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();
        var exception = await Record.ExceptionAsync(() => migrator.MigrateAsync(
            AuthPersistenceMigrationTestBase.FullName(dbContext, "AddAuthThrottleCounters")));

        var postgres = Assert.IsType<PostgresException>(exception);
        Assert.Equal("23001", postgres.SqlState);
        Assert.Contains("audit history", postgres.MessageText);

        // The failed migration was rolled back: the table, its row, its triggers and the history entry remain.
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        Assert.True(await AuthPersistenceMigrationTestBase.TableExistsAsync(connection, "audit_events"));
        Assert.Equal(1, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM audit_events WHERE id = @id;", ("id", row["id"]!)));
        Assert.Equal(2, await PostgresAssert.CountAsync(connection, null,
            "SELECT COUNT(*) FROM pg_trigger WHERE tgrelid = to_regclass('audit_events') AND NOT tgisinternal;"));
    }
}

/// <summary>
/// The runtime privilege self-check query (repository ADR 0006 §7.3, E42). The test
/// role owns the tables, so the report must show ownership and every privilege; the
/// four privileges are exactly the ones the migration revokes.
/// </summary>
public class AuditPrivilegeSelfCheckTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public AuditPrivilegeSelfCheckTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Query_ReportsTheOwnerWithEveryPrivilege_AsUnsafe()
    {
        await using var dbContext = _fixture.CreateDbContext();

        var report = Assert.Single(await AuditPrivilegeSelfCheck.QueryAsync(dbContext));

        Assert.Equal("audit_events", report.Table);
        Assert.True(report.IsOwner);
        // The owner is not also reported as a non-owning member of itself.
        Assert.False(report.HasOwnerRoleMembership);
        Assert.True(report.CanUpdate);
        Assert.True(report.CanDelete);
        Assert.True(report.CanTruncate);
        Assert.True(report.CanTrigger);
        Assert.True(report.IsUnsafe);
    }

    [Fact]
    public async Task Query_ReportsEveryAuditTable_AndIsReadOnly()
    {
        await using var dbContext = _fixture.CreateDbContext();
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var before = await PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM audit_events;");

        var reports = await AuditPrivilegeSelfCheck.QueryAsync(dbContext);

        Assert.Equal(AuditPrivilegeSelfCheck.AuditTables, reports.Select(r => r.Table));
        Assert.Equal(before, await PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM audit_events;"));
    }

    [Fact]
    public async Task Query_LeavesAnOpenConnectionOpen_AndClosesOneItOpened()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var connection = dbContext.Database.GetDbConnection();

        await AuditPrivilegeSelfCheck.QueryAsync(dbContext);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);

        await connection.OpenAsync();
        await AuditPrivilegeSelfCheck.QueryAsync(dbContext);
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Theory]
    [InlineData(true, false, false, false, false, false, false)]
    [InlineData(false, true, false, false, false, false, false)]
    [InlineData(false, false, true, false, false, false, false)]
    [InlineData(false, false, false, true, false, false, false)]
    [InlineData(false, false, false, false, true, false, false)]
    [InlineData(false, false, false, false, false, true, false)]
    [InlineData(false, false, false, false, false, false, true)]
    public void AnySinglePrivilegeOwnershipMembershipOrSuperuser_IsUnsafe(
        bool update, bool delete, bool truncate, bool trigger, bool owner, bool hasOwnerRoleMembership, bool superuser) =>
        Assert.True(new AuditPrivilegeReport("audit_events", update, delete, truncate, trigger, owner, hasOwnerRoleMembership, superuser).IsUnsafe);

    [Fact]
    public void NoPrivilegeNoOwnershipNoMembershipNoSuperuser_IsSafe() =>
        Assert.False(new AuditPrivilegeReport("audit_events", false, false, false, false, false, false, false).IsUnsafe);
}
