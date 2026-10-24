using Contractra.Domain.Identity;
using Contractra.Domain.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Rolls back the whole user identity slice - AddUsers through AddUserPartyLinks -
/// in one step and re-applies it. Runs in its own ephemeral database (own
/// fixture) because it migrates the schema down and back up.
/// </summary>
public class UserIdentitySliceMigrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string LastMigrationBeforeTheSlice = "20260922195442_AddAuthorizationCatalog";

    private const string SliceTables = "'users', 'user_roles', 'user_permissions', 'user_party_links'";

    private const string SliceConstraints =
        "'pk_users', 'ck_users_status_valid_values', 'ck_users_password_hash_not_empty', 'ck_users_security_version_positive', " +
        "'ck_users_temporary_password_requires_change', 'ck_users_bootstrap_admin_never_must_change', " +
        "'pk_user_roles', 'fk_user_roles_users_user_id', 'fk_user_roles_roles_role_id', 'ck_user_roles_ended_at_after_assigned_at', 'ex_user_roles_no_overlapping_period', " +
        "'pk_user_permissions', 'fk_user_permissions_users_user_id', 'fk_user_permissions_permissions_permission_id', 'ck_user_permissions_ended_at_after_granted_at', 'ex_user_permissions_no_overlapping_period', " +
        "'pk_user_party_links', 'fk_user_party_links_users_user_id', 'fk_user_party_links_parties_party_id'";

    private const string SliceIndexes =
        "'ux_users_login_identifier', 'ux_users_login_identifier_lower', 'ux_users_is_bootstrap_admin', " +
        "'ux_user_roles_user_id_role_id_active', 'ix_user_roles_role_id', " +
        "'ux_user_permissions_user_id_permission_id_active', 'ix_user_permissions_permission_id', " +
        "'ux_user_party_links_user_id', 'ix_user_party_links_party_id', 'ux_user_party_links_party_id'";

    private readonly PostgresTestDatabaseFixture _fixture;

    public UserIdentitySliceMigrationTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Down_RemovesTheWholeSlice_KeepsPartyAndCatalogObjectsAndData_AndUpReappliesCleanly()
    {
        var party = new Party("م. طرف قبل الترحيل", PartyStatus.Active);
        // Not "Engineer": Sprint 2's SeedAuthorizationCatalog migration (which
        // sits after AddAuthorizationCatalog, inside the slice this test rolls
        // back and reapplies) now seeds that exact code as a permanent row in
        // the fixture's already-fully-migrated starting state.
        var role = new Role(new RoleCode("PreSliceTestRole"), RoleStatus.Active);
        await using (var seedContext = _fixture.CreateDbContext())
        {
            seedContext.Parties.Add(party);
            seedContext.Roles.Add(role);
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();

        await migrator.MigrateAsync(LastMigrationBeforeTheSlice);

        await using (var connection = await OpenConnectionAsync())
        {
            Assert.Equal(0, await CountAsync(connection,
                $"SELECT COUNT(*) FROM pg_class WHERE relname IN ({SliceTables});"));
            Assert.Equal(0, await CountAsync(connection,
                $"SELECT COUNT(*) FROM pg_constraint WHERE conname IN ({SliceConstraints});"));
            Assert.Equal(0, await CountAsync(connection,
                $"SELECT COUNT(*) FROM pg_class WHERE relname IN ({SliceIndexes});"));

            // Objects and data owned by InitialPartySlice and AddAuthorizationCatalog survive.
            Assert.Equal(7, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_class WHERE relkind = 'r' AND relname IN ('parties', 'party_roles', 'party_aliases', 'party_identifiers', 'roles', 'permissions', 'role_permissions');"));
            Assert.Equal(1, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_constraint WHERE conname = 'ex_party_roles_no_overlapping_period';"));
            Assert.Equal(2, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_indexes WHERE indexname IN ('ux_roles_code_lower', 'ux_permissions_code_lower');"));
            Assert.Equal(2, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_trigger WHERE tgname IN ('trg_role_permissions_forbid_payroll_view', 'trg_permissions_forbid_granted_rename_to_payroll_view');"));
            Assert.Equal(1, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_extension WHERE extname = 'btree_gist';"));
            Assert.Equal(1, await PostgresAssert.CountAsync(connection, null,
                "SELECT COUNT(*) FROM parties WHERE id = @id;", ("id", party.Id)));
            Assert.Equal(1, await PostgresAssert.CountAsync(connection, null,
                "SELECT COUNT(*) FROM roles WHERE id = @id;", ("id", role.Id)));
        }

        await migrator.MigrateAsync();

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        await using (var connection = await OpenConnectionAsync())
        {
            Assert.Equal(4, await CountAsync(connection,
                $"SELECT COUNT(*) FROM pg_class WHERE relkind = 'r' AND relname IN ({SliceTables});"));
            Assert.Equal(2, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_constraint WHERE conname IN ('ex_user_roles_no_overlapping_period', 'ex_user_permissions_no_overlapping_period');"));
            Assert.Equal(1, await CountAsync(connection,
                "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ux_users_login_identifier_lower';"));
        }

        // The re-created schema is usable end to end.
        var user = TestUsers.NewUser("After.Reapply", UserStatus.Active);
        var at = new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero);
        user.AssignRole(role, at, at);
        user.LinkToParty(party);
        await using (var writeContext = _fixture.CreateDbContext())
        {
            writeContext.Users.Add(user);
            await writeContext.SaveChangesAsync();
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var stored = await verifyContext.Users.Include(u => u.Roles).Include(u => u.PartyLink).SingleAsync(u => u.Id == user.Id);
        Assert.Single(stored.Roles);
        Assert.Equal(party.Id, stored.PartyLink!.PartyId);
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
