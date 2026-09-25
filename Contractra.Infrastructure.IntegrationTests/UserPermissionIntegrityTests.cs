using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

public class UserPermissionIntegrityTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private const string InsertUserPermission =
        "INSERT INTO user_permissions (id, user_id, permission_id, granted_at, ended_at) VALUES (@id, @user_id, @permission_id, @granted_at, @ended_at);";

    private readonly PostgresTestDatabaseFixture _fixture;

    public UserPermissionIntegrityTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PayrollView_GrantedPerUser_IsAcceptedByTheDatabase_ThroughEfCore()
    {
        // BR-235 / FR-AUTH-006: the per-user path is the only sanctioned path for
        // Payroll.View; role_permissions forbids it (ADR 0002 §6), this must not.
        var user = await SeedUserAsync();
        var payrollView = await SeedPermissionAsync(PermissionCode.PayrollView);
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var tracked = await dbContext.Users.Include(u => u.Permissions).SingleAsync(u => u.Id == user.Id);
            tracked.GrantPermission(payrollView, T0, T0);
            await dbContext.SaveChangesAsync();
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var stored = await verifyContext.Users.Include(u => u.Permissions).SingleAsync(u => u.Id == user.Id);
        Assert.Equal(payrollView.Id, Assert.Single(stored.Permissions).PermissionId);
    }

    [Fact]
    public async Task PayrollView_GrantedPerUser_IsAcceptedByTheDatabase_ThroughRawSql()
    {
        var user = await SeedUserAsync();
        var payrollView = await SeedPermissionAsync(PermissionCode.PayrollView);
        await using var connection = await OpenConnectionAsync();

        Assert.Equal(1, await InsertAsync(connection, user.Id, payrollView.Id, T0, null));
    }

    [Fact]
    public async Task GrantedAndRevokedPermissions_RoundTripThroughEfCore()
    {
        var user = await SeedUserAsync();
        var permission = await SeedPermissionAsync();
        await using (var dbContext = _fixture.CreateDbContext())
        {
            (await dbContext.Users.Include(u => u.Permissions).SingleAsync(u => u.Id == user.Id))
                .GrantPermission(permission, T0, T0);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            (await dbContext.Users.Include(u => u.Permissions).SingleAsync(u => u.Id == user.Id))
                .Permissions.Single().Revoke(T0.AddDays(3), T0.AddDays(3));
            await dbContext.SaveChangesAsync();
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var grant = Assert.Single((await verifyContext.Users.Include(u => u.Permissions).SingleAsync(u => u.Id == user.Id)).Permissions);
        Assert.Equal(T0, grant.GrantedAt);
        Assert.Equal(T0.AddDays(3), grant.RevokedAt);
    }

    [Fact]
    public async Task TwoActiveGrants_OfTheSamePermission_ViolateThePartialUniqueIndex()
    {
        var user = await SeedUserAsync();
        var permission = await SeedPermissionAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, permission.Id, T0, null);

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, user.Id, permission.Id, T0.AddDays(5), null),
            "23505",
            "ux_user_permissions_user_id_permission_id_active");
    }

    [Fact]
    public async Task SecondActiveGrant_SavedThroughEfCore_IsRejectedByTheDatabase_WhenGrantsWereNotLoaded()
    {
        var user = await SeedUserAsync();
        var permission = await SeedPermissionAsync();
        await using (var dbContext = _fixture.CreateDbContext())
        {
            (await dbContext.Users.Include(u => u.Permissions).SingleAsync(u => u.Id == user.Id))
                .GrantPermission(permission, T0, T0);
            await dbContext.SaveChangesAsync();
        }

        // Grants are not loaded here, so the domain's overlap check cannot see the
        // existing grant; the database is the final guard.
        await using var dbContext2 = _fixture.CreateDbContext();
        (await dbContext2.Users.SingleAsync(u => u.Id == user.Id)).GrantPermission(permission, T0.AddDays(1), T0.AddDays(1));

        await PostgresAssert.FailsWithAsync(() => dbContext2.SaveChangesAsync(), "23505", "ux_user_permissions_user_id_permission_id_active");
    }

    [Fact]
    public async Task RevokedGrant_PlusANewActiveOne_OfTheSamePermission_IsAllowed()
    {
        var user = await SeedUserAsync();
        var permission = await SeedPermissionAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, permission.Id, T0, T0.AddDays(10));

        Assert.Equal(1, await InsertAsync(connection, user.Id, permission.Id, T0.AddDays(20), null));
    }

    [Fact]
    public async Task Regrant_AtTheExactInstantThePreviousGrantWasRevoked_IsAllowed()
    {
        var user = await SeedUserAsync();
        var permission = await SeedPermissionAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, permission.Id, T0, T0.AddDays(10));

        Assert.Equal(1, await InsertAsync(connection, user.Id, permission.Id, T0.AddDays(10), null));
    }

    [Fact]
    public async Task BackdatedRegrant_OverlappingARevokedOne_ViolatesTheExclusionConstraint()
    {
        var user = await SeedUserAsync();
        var permission = await SeedPermissionAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, permission.Id, T0, T0.AddDays(10));

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, user.Id, permission.Id, T0.AddDays(5), null),
            "23P01",
            "ex_user_permissions_no_overlapping_period");
    }

    [Fact]
    public async Task EndedAt_BeforeGrantedAt_ViolatesTheCheckConstraint()
    {
        var user = await SeedUserAsync();
        var permission = await SeedPermissionAsync();
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, user.Id, permission.Id, T0, T0.AddMicroseconds(-1)),
            "23514",
            "ck_user_permissions_ended_at_after_granted_at");
    }

    [Fact]
    public async Task ZeroLengthGrant_RevokedAtItsOwnStart_IsAllowedByTheCheckConstraint()
    {
        var user = await SeedUserAsync();
        var permission = await SeedPermissionAsync();
        await using var connection = await OpenConnectionAsync();

        Assert.Equal(1, await InsertAsync(connection, user.Id, permission.Id, T0, T0));
    }

    [Fact]
    public async Task GrantToAMissingUser_ViolatesTheUserForeignKey()
    {
        var permission = await SeedPermissionAsync();
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, Guid.CreateVersion7(), permission.Id, T0, null),
            "23503",
            "fk_user_permissions_users_user_id");
    }

    [Fact]
    public async Task GrantOfAMissingPermission_ViolatesThePermissionForeignKey()
    {
        var user = await SeedUserAsync();
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, user.Id, Guid.CreateVersion7(), T0, null),
            "23503",
            "fk_user_permissions_permissions_permission_id");
    }

    [Fact]
    public async Task DeletingAReferencedUser_IsRestricted()
    {
        var user = await SeedUserAsync();
        var permission = await SeedPermissionAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, permission.Id, T0, null);

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM users WHERE id = @id;", ("id", user.Id)),
            "23001", // restrict_violation (ON DELETE RESTRICT)
            "fk_user_permissions_users_user_id");
    }

    [Fact]
    public async Task DeletingAReferencedPermission_IsRestricted()
    {
        var user = await SeedUserAsync();
        var permission = await SeedPermissionAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, permission.Id, T0, null);

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM permissions WHERE id = @id;", ("id", permission.Id)),
            "23001", // restrict_violation (ON DELETE RESTRICT)
            "fk_user_permissions_permissions_permission_id");
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User(new LoginIdentifier($"UserPermission.{Guid.NewGuid():N}"), UserStatus.Active);
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user;
    }

    // Permission codes are unique case-insensitively (ux_permissions_code_lower), and
    // tests share one database, so a named code such as Payroll.View is seeded once
    // and reused by exact spelling.
    private async Task<Permission> SeedPermissionAsync(string? code = null)
    {
        await using var dbContext = _fixture.CreateDbContext();
        var existing = code is null
            ? null
            : await dbContext.Permissions.SingleOrDefaultAsync(p => p.Code == new PermissionCode(code));
        if (existing is not null)
        {
            return existing;
        }

        var permission = new Permission(new PermissionCode(code ?? $"Test.{Guid.NewGuid():N}"), PermissionStatus.Active);
        dbContext.Permissions.Add(permission);
        await dbContext.SaveChangesAsync();
        return permission;
    }

    private static Task<int> InsertAsync(
        NpgsqlConnection connection, Guid userId, Guid permissionId, DateTimeOffset grantedAt, DateTimeOffset? endedAt) =>
        PostgresAssert.ExecuteAsync(connection, null, InsertUserPermission,
            ("id", Guid.CreateVersion7()),
            ("user_id", userId),
            ("permission_id", permissionId),
            ("granted_at", grantedAt),
            ("ended_at", endedAt.HasValue ? endedAt.Value : DBNull.Value));

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
