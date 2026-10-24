using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Verifies the Sprint 2 authorization catalog seed (US-BE-006 / TASK-BE-024):
/// the four roles, the six permissions from Confirmed Decision C2 as amended
/// by owner decision R13 (the Identity-active four plus Apps.Admin.Access and
/// Apps.Employee.Access), exactly seven role_permissions rows, and
/// Payroll.View excluded from every role - never bypassing the existing
/// ADR 0002 guard.
/// </summary>
public class AuthorizationCatalogSeedTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private static readonly string[] ExpectedRoleCodes = { "Engineer", "OfficeStaff", "Accountant", "Admin" };

    private static readonly string[] ExpectedPermissionCodes =
    {
        "Users.Manage", "Permissions.Grant", "Roles.Manage", "Payroll.View",
        "Apps.Admin.Access", "Apps.Employee.Access",
    };

    // Default grants per role: spec §7.3 for the Identity permissions (Admin
    // only), owner decision R13 for the two app-access permissions.
    public static TheoryData<string, string[]> ExpectedGrantsByRole => new()
    {
        { "Admin", new[] { "Apps.Admin.Access", "Users.Manage", "Permissions.Grant", "Roles.Manage" } },
        { "OfficeStaff", new[] { "Apps.Admin.Access" } },
        { "Accountant", new[] { "Apps.Admin.Access" } },
        { "Engineer", new[] { "Apps.Employee.Access" } },
    };

    private readonly PostgresTestDatabaseFixture _fixture;

    public AuthorizationCatalogSeedTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Seed_CreatesExactlyTheFourExpectedRoles()
    {
        await using var dbContext = _fixture.CreateDbContext();

        // ToListAsync() first, then project client-side: Code is a converted
        // value object and its .Value member cannot be translated to SQL.
        var roleCodes = (await dbContext.Roles.ToListAsync()).Select(r => r.Code.Value).ToList();

        Assert.Equal(ExpectedRoleCodes.Length, roleCodes.Count);
        Assert.Equal(ExpectedRoleCodes.OrderBy(c => c), roleCodes.OrderBy(c => c));
    }

    [Fact]
    public async Task Seed_CreatesExactlyTheSixExpectedPermissions()
    {
        await using var dbContext = _fixture.CreateDbContext();

        var permissionCodes = (await dbContext.Permissions.ToListAsync()).Select(p => p.Code.Value).ToList();

        Assert.Equal(ExpectedPermissionCodes.Length, permissionCodes.Count);
        Assert.Equal(ExpectedPermissionCodes.OrderBy(c => c), permissionCodes.OrderBy(c => c));
    }

    [Theory]
    [MemberData(nameof(ExpectedGrantsByRole))]
    public async Task Seed_GrantsEachRole_ExactlyItsDefaultPermissions(string roleCode, string[] expectedCodes)
    {
        await using var dbContext = _fixture.CreateDbContext();

        var role = await dbContext.Roles.Include(r => r.Permissions).SingleAsync(r => r.Code == new RoleCode(roleCode));
        var grantedPermissionIds = role.Permissions.Select(rp => rp.PermissionId).ToList();
        var grantedCodes = (await dbContext.Permissions
            .Where(p => grantedPermissionIds.Contains(p.Id))
            .ToListAsync())
            .Select(p => p.Code.Value)
            .ToList();

        Assert.Equal(expectedCodes.OrderBy(c => c), grantedCodes.OrderBy(c => c));
    }

    [Fact]
    public async Task Seed_CreatesExactlySevenRolePermissionGrants()
    {
        await using var dbContext = _fixture.CreateDbContext();

        Assert.Equal(7, await dbContext.Set<RolePermission>().CountAsync());
    }

    [Fact]
    public async Task Seed_GrantsPayrollView_ToNoRole()
    {
        // BR-235 / Confirmed Decision C3: Payroll.View must never be a role
        // grant, seeded or otherwise.
        await using var dbContext = _fixture.CreateDbContext();

        var payrollView = await dbContext.Permissions.SingleAsync(p => p.Code == new PermissionCode("Payroll.View"));
        var grantCount = await dbContext.Set<RolePermission>().CountAsync(rp => rp.PermissionId == payrollView.Id);

        Assert.Equal(0, grantCount);
    }

    [Fact]
    public async Task ExistingPayrollViewGuardTrigger_StillRejectsARawSqlRoleGrant_AfterTheSeed()
    {
        // Proves the seed does not weaken or bypass the ADR 0002 guard: even
        // after seeding, a raw-SQL attempt to grant Payroll.View to a role
        // still fails exactly as PayrollViewGuardTests already proves it does
        // without this seed.
        await using var dbContext = _fixture.CreateDbContext();
        var admin = await dbContext.Roles.SingleAsync(r => r.Code == new RoleCode("Admin"));
        var payrollView = await dbContext.Permissions.SingleAsync(p => p.Code == new PermissionCode("Payroll.View"));

        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(
                connection,
                null,
                "INSERT INTO role_permissions (id, role_id, permission_id) VALUES (@id, @role_id, @permission_id);",
                ("id", Guid.CreateVersion7()),
                ("role_id", admin.Id),
                ("permission_id", payrollView.Id)),
            "23514",
            "trg_role_permissions_forbid_payroll_view");
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}

/// <summary>
/// Runs in its own ephemeral database (own fixture) because it migrates the
/// schema down and back up, unlike the read-only assertions in
/// AuthorizationCatalogSeedTests, which share one seeded database.
/// </summary>
public class AuthorizationCatalogSeedMigrationTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string PreviousMigration = "20260922195442_AddAuthorizationCatalog";

    private readonly PostgresTestDatabaseFixture _fixture;

    public AuthorizationCatalogSeedMigrationTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Down_RemovesTheSeededRows_AndUpReappliesTheSameState()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);

        await using (var connection = await OpenConnectionAsync())
        {
            Assert.Equal(0, await PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM roles;"));
            Assert.Equal(0, await PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM permissions;"));
            Assert.Equal(0, await PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM role_permissions;"));
        }

        await migrator.MigrateAsync();

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        await using (var connection = await OpenConnectionAsync())
        {
            Assert.Equal(4, await PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM roles;"));
            Assert.Equal(6, await PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM permissions;"));
            Assert.Equal(7, await PostgresAssert.CountAsync(connection, null, "SELECT COUNT(*) FROM role_permissions;"));
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
