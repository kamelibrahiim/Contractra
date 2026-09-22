using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

public class RolePermissionIntegrityTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _fixture;

    public RolePermissionIntegrityTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GrantedPermission_IsPersistedAndLoadedWithTheRole()
    {
        var (role, permission) = await CreateGrantedPairAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var storedRole = await verifyContext.Roles
            .Include(r => r.Permissions)
            .SingleAsync(r => r.Id == role.Id);

        var grant = Assert.Single(storedRole.Permissions);
        Assert.Equal(permission.Id, grant.PermissionId);
    }

    [Fact]
    public async Task RoleWithZeroPermissions_CanBePersisted()
    {
        var role = new Role(new RoleCode($"Empty.{Guid.NewGuid():N}"), RoleStatus.Active);

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Roles.Add(role);
        await dbContext.SaveChangesAsync();

        await using var verifyContext = _fixture.CreateDbContext();
        var storedRole = await verifyContext.Roles.Include(r => r.Permissions).SingleAsync(r => r.Id == role.Id);
        Assert.Empty(storedRole.Permissions);
    }

    [Fact]
    public async Task DuplicateRoleIdAndPermissionId_IsRejected_ThroughTheDomainWhenGrantsAreNotLoaded()
    {
        var (role, permission) = await CreateGrantedPairAsync();

        // Loaded without its grants, the domain cannot see the existing grant;
        // the unique index is the final guard.
        await using var dbContext = _fixture.CreateDbContext();
        var reloadedRole = await dbContext.Roles.SingleAsync(r => r.Id == role.Id);
        var reloadedPermission = await dbContext.Permissions.SingleAsync(p => p.Id == permission.Id);
        dbContext.RolePermissions.Add(reloadedRole.GrantPermission(reloadedPermission));

        await PostgresAssert.FailsWithAsync(
            () => dbContext.SaveChangesAsync(),
            "23505",
            "ux_role_permissions_role_id_permission_id");
    }

    [Fact]
    public async Task DuplicateRoleIdAndPermissionId_IsRejected_ThroughRawSql()
    {
        var (role, permission) = await CreateGrantedPairAsync();

        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(
                connection,
                null,
                "INSERT INTO role_permissions (id, role_id, permission_id) VALUES (@id, @role, @permission);",
                ("id", Guid.CreateVersion7()),
                ("role", role.Id),
                ("permission", permission.Id)),
            "23505",
            "ux_role_permissions_role_id_permission_id");
    }

    [Fact]
    public async Task Restrict_PreventsDeletingAReferencedRole()
    {
        var (role, _) = await CreateGrantedPairAsync();

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Roles.Remove(await dbContext.Roles.SingleAsync(r => r.Id == role.Id));

        await PostgresAssert.FailsWithAsync(
            () => dbContext.SaveChangesAsync(),
            "23001",
            "fk_role_permissions_roles_role_id");

        await using var verifyContext = _fixture.CreateDbContext();
        Assert.True(await verifyContext.Roles.AnyAsync(r => r.Id == role.Id));
        Assert.True(await verifyContext.RolePermissions.AnyAsync(rp => rp.RoleId == role.Id));
    }

    [Fact]
    public async Task Restrict_PreventsDeletingAReferencedPermission()
    {
        var (_, permission) = await CreateGrantedPairAsync();

        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(
                connection,
                null,
                "DELETE FROM permissions WHERE id = @id;",
                ("id", permission.Id)),
            "23001",
            "fk_role_permissions_permissions_permission_id");

        Assert.Equal(1, await PostgresAssert.CountAsync(
            connection, null, "SELECT COUNT(*) FROM permissions WHERE id = @id;", ("id", permission.Id)));
    }

    [Fact]
    public async Task GrantToAMissingRoleOrPermission_IsRejectedByTheForeignKeys()
    {
        var (role, permission) = await CreateGrantedPairAsync();
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(
                connection,
                null,
                "INSERT INTO role_permissions (id, role_id, permission_id) VALUES (@id, @role, @permission);",
                ("id", Guid.CreateVersion7()),
                ("role", Guid.CreateVersion7()),
                ("permission", permission.Id)),
            "23503",
            "fk_role_permissions_roles_role_id");

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(
                connection,
                null,
                "INSERT INTO role_permissions (id, role_id, permission_id) VALUES (@id, @role, @permission);",
                ("id", Guid.CreateVersion7()),
                ("role", role.Id),
                ("permission", Guid.CreateVersion7())),
            "23503",
            "fk_role_permissions_permissions_permission_id");
    }

    private async Task<(Role Role, Permission Permission)> CreateGrantedPairAsync()
    {
        var role = new Role(new RoleCode($"Role.{Guid.NewGuid():N}"), RoleStatus.Active);
        var permission = new Permission(new PermissionCode($"Permission.{Guid.NewGuid():N}"), PermissionStatus.Active);
        role.GrantPermission(permission);

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Permissions.Add(permission);
        dbContext.Roles.Add(role);
        await dbContext.SaveChangesAsync();

        return (role, permission);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
