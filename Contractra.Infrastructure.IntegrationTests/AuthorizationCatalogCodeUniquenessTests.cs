using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

public class AuthorizationCatalogCodeUniquenessTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public AuthorizationCatalogCodeUniquenessTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("UniqueRoleA", "UniqueRoleA")]
    [InlineData("UniqueRoleB", "uniquerolEB")]
    [InlineData("UniqueRoleC", "UNIQUEROLEC")]
    public async Task RoleCode_IsUnique_CaseInsensitively(string existing, string duplicate)
    {
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Roles.Add(new Role(new RoleCode(existing), RoleStatus.Active));
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.Roles.Add(new Role(new RoleCode(duplicate), RoleStatus.Active));

        await PostgresAssert.FailsWithAsync(() => dbContext2.SaveChangesAsync(), "23505", "ux_roles_code_lower");
    }

    [Theory]
    [InlineData("Unique.PermissionA", "Unique.PermissionA")]
    [InlineData("Unique.PermissionB", "unique.permissionb")]
    [InlineData("Unique.PermissionC", "UNIQUE.PERMISSIONC")]
    public async Task PermissionCode_IsUnique_CaseInsensitively(string existing, string duplicate)
    {
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Permissions.Add(new Permission(new PermissionCode(existing), PermissionStatus.Active));
        await dbContext.SaveChangesAsync();

        await using var dbContext2 = _fixture.CreateDbContext();
        dbContext2.Permissions.Add(new Permission(new PermissionCode(duplicate), PermissionStatus.Active));

        await PostgresAssert.FailsWithAsync(() => dbContext2.SaveChangesAsync(), "23505", "ux_permissions_code_lower");
    }

    [Fact]
    public async Task RoleAndPermission_MayShareACode_BecauseUniquenessIsPerTable()
    {
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Roles.Add(new Role(new RoleCode("Shared.Code"), RoleStatus.Active));
        dbContext.Permissions.Add(new Permission(new PermissionCode("Shared.Code"), PermissionStatus.Active));

        await dbContext.SaveChangesAsync(); // must not throw
    }

    [Fact]
    public async Task StoredCodes_KeepTheSuppliedCasing()
    {
        // "CasedOfficeStaff"/"Reports.Monthly.View.Cased" rather than the bare
        // documented spellings: Sprint 2's SeedAuthorizationCatalog migration
        // now seeds "OfficeStaff" as a permanent, committed row in every
        // ephemeral test database, so inserting it again here would collide on
        // ux_roles_code_lower regardless of this test's own casing. The point
        // under test - that casing is preserved exactly as supplied - does not
        // depend on using a real business role/permission name.
        var role = new Role(new RoleCode("CasedOfficeStaff"), RoleStatus.Active);
        var permission = new Permission(new PermissionCode("Reports.Monthly.View.Cased"), PermissionStatus.Deprecated);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            dbContext.Roles.Add(role);
            dbContext.Permissions.Add(permission);
            await dbContext.SaveChangesAsync();
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var storedRole = await verifyContext.Roles.SingleAsync(r => r.Id == role.Id);
        var storedPermission = await verifyContext.Permissions.SingleAsync(p => p.Id == permission.Id);

        Assert.Equal("CasedOfficeStaff", storedRole.Code.Value);
        Assert.Equal(RoleStatus.Active, storedRole.Status);
        Assert.Equal("Reports.Monthly.View.Cased", storedPermission.Code.Value);
        Assert.Equal(PermissionStatus.Deprecated, storedPermission.Status);
    }

    [Theory]
    [InlineData("roles", "ck_roles_status_valid_values", "Disabled")]
    [InlineData("roles", "ck_roles_status_valid_values", "active")]
    [InlineData("permissions", "ck_permissions_status_valid_values", "Revoked")]
    [InlineData("permissions", "ck_permissions_status_valid_values", "deprecated")]
    public async Task StatusCheckConstraint_RejectsUndocumentedValues(string table, string constraint, string status)
    {
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(
                connection,
                null,
                $"INSERT INTO {table} (id, code, status) VALUES (@id, @code, @status);",
                ("id", Guid.CreateVersion7()),
                ("code", $"StatusCheck.{Guid.NewGuid():N}"),
                ("status", status)),
            "23514",
            constraint);
    }

    [Theory]
    [InlineData("roles", "Active")]
    [InlineData("roles", "Deprecated")]
    [InlineData("permissions", "Active")]
    [InlineData("permissions", "Deprecated")]
    public async Task StatusCheckConstraint_AcceptsDocumentedValues(string table, string status)
    {
        await using var connection = await OpenConnectionAsync();

        var inserted = await PostgresAssert.ExecuteAsync(
            connection,
            null,
            $"INSERT INTO {table} (id, code, status) VALUES (@id, @code, @status);",
            ("id", Guid.CreateVersion7()),
            ("code", $"StatusCheck.{Guid.NewGuid():N}"),
            ("status", status));

        Assert.Equal(1, inserted);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
