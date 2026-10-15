using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

public class UserRoleIntegrityTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private const string InsertUserRole =
        "INSERT INTO user_roles (id, user_id, role_id, assigned_at, ended_at) VALUES (@id, @user_id, @role_id, @assigned_at, @ended_at);";

    private readonly PostgresTestDatabaseFixture _fixture;

    public UserRoleIntegrityTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AssignedAndEndedRoles_RoundTripThroughEfCore()
    {
        var (user, role) = await SeedUserAndRoleAsync();
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var tracked = await dbContext.Users.Include(u => u.Roles).SingleAsync(u => u.Id == user.Id);
            tracked.AssignRole(role, T0, T0);
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var tracked = await dbContext.Users.Include(u => u.Roles).SingleAsync(u => u.Id == user.Id);
            tracked.EndRole(role, T0.AddDays(3), T0.AddDays(3));
            await dbContext.SaveChangesAsync();
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var stored = await verifyContext.Users.Include(u => u.Roles).SingleAsync(u => u.Id == user.Id);
        var userRole = Assert.Single(stored.Roles);
        Assert.Equal(role.Id, userRole.RoleId);
        Assert.Equal(T0, userRole.AssignedAt);
        Assert.Equal(T0.AddDays(3), userRole.EndedAt);
    }

    [Fact]
    public async Task TwoOpenAssignments_OfTheSameRole_ViolateThePartialUniqueIndex()
    {
        var (user, role) = await SeedUserAndRoleAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, role.Id, T0, null);

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, user.Id, role.Id, T0.AddDays(5), null),
            "23505",
            "ux_user_roles_user_id_role_id_active");
    }

    [Fact]
    public async Task EndedAssignment_PlusANewOpenOne_OfTheSameRole_IsAllowed()
    {
        var (user, role) = await SeedUserAndRoleAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, role.Id, T0, T0.AddDays(10));

        Assert.Equal(1, await InsertAsync(connection, user.Id, role.Id, T0.AddDays(20), null));
    }

    [Fact]
    public async Task Reassignment_AtTheExactInstantThePreviousOneEnded_IsAllowed()
    {
        var (user, role) = await SeedUserAndRoleAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, role.Id, T0, T0.AddDays(10));

        Assert.Equal(1, await InsertAsync(connection, user.Id, role.Id, T0.AddDays(10), null));
    }

    [Fact]
    public async Task BackdatedReassignment_OverlappingAnEndedOne_ViolatesTheExclusionConstraint()
    {
        var (user, role) = await SeedUserAndRoleAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, role.Id, T0, T0.AddDays(10));

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, user.Id, role.Id, T0.AddDays(5), null),
            "23P01",
            "ex_user_roles_no_overlapping_period");
    }

    [Fact]
    public async Task DifferentRoles_MayBeHeldAtTheSameTime()
    {
        var (user, role) = await SeedUserAndRoleAsync();
        var otherRole = await SeedRoleAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, role.Id, T0, null);

        Assert.Equal(1, await InsertAsync(connection, user.Id, otherRole.Id, T0, null));
    }

    [Fact]
    public async Task EndedAt_BeforeAssignedAt_ViolatesTheCheckConstraint()
    {
        var (user, role) = await SeedUserAndRoleAsync();
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, user.Id, role.Id, T0, T0.AddMicroseconds(-1)),
            "23514",
            "ck_user_roles_ended_at_after_assigned_at");
    }

    [Fact]
    public async Task ZeroLengthAssignment_EndedAtItsOwnStart_IsAllowedByTheCheckConstraint()
    {
        var (user, role) = await SeedUserAndRoleAsync();
        await using var connection = await OpenConnectionAsync();

        Assert.Equal(1, await InsertAsync(connection, user.Id, role.Id, T0, T0));
    }

    [Fact]
    public async Task SecondOpenAssignment_SavedThroughEfCore_IsRejectedByTheDatabase_WhenRolesWereNotLoaded()
    {
        var (user, role) = await SeedUserAndRoleAsync();
        await using (var dbContext = _fixture.CreateDbContext())
        {
            (await dbContext.Users.Include(u => u.Roles).SingleAsync(u => u.Id == user.Id)).AssignRole(role, T0, T0);
            await dbContext.SaveChangesAsync();
        }

        // Roles are not loaded here, so the domain's overlap check cannot see the
        // existing assignment; the database is the final guard.
        await using var dbContext2 = _fixture.CreateDbContext();
        var withoutRoles = await dbContext2.Users.SingleAsync(u => u.Id == user.Id);
        withoutRoles.AssignRole(role, T0.AddDays(1), T0.AddDays(1));

        await PostgresAssert.FailsWithAsync(() => dbContext2.SaveChangesAsync(), "23505", "ux_user_roles_user_id_role_id_active");
    }

    [Fact]
    public async Task AssignmentToAMissingUser_ViolatesTheUserForeignKey()
    {
        var (_, role) = await SeedUserAndRoleAsync();
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, Guid.CreateVersion7(), role.Id, T0, null),
            "23503",
            "fk_user_roles_users_user_id");
    }

    [Fact]
    public async Task AssignmentToAMissingRole_ViolatesTheRoleForeignKey()
    {
        var (user, _) = await SeedUserAndRoleAsync();
        await using var connection = await OpenConnectionAsync();

        await PostgresAssert.FailsWithAsync(
            () => InsertAsync(connection, user.Id, Guid.CreateVersion7(), T0, null),
            "23503",
            "fk_user_roles_roles_role_id");
    }

    [Fact]
    public async Task DeletingAReferencedUser_IsRestricted()
    {
        var (user, role) = await SeedUserAndRoleAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, role.Id, T0, null);

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM users WHERE id = @id;", ("id", user.Id)),
            "23001", // restrict_violation (ON DELETE RESTRICT)
            "fk_user_roles_users_user_id");
    }

    [Fact]
    public async Task DeletingAReferencedRole_IsRestricted()
    {
        var (user, role) = await SeedUserAndRoleAsync();
        await using var connection = await OpenConnectionAsync();
        await InsertAsync(connection, user.Id, role.Id, T0, null);

        await PostgresAssert.FailsWithAsync(
            () => PostgresAssert.ExecuteAsync(connection, null, "DELETE FROM roles WHERE id = @id;", ("id", role.Id)),
            "23001", // restrict_violation (ON DELETE RESTRICT)
            "fk_user_roles_roles_role_id");
    }

    private async Task<(User User, Role Role)> SeedUserAndRoleAsync()
    {
        var user = TestUsers.NewUser($"UserRole.{Guid.NewGuid():N}", UserStatus.Active);
        await using (var dbContext = _fixture.CreateDbContext())
        {
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }

        return (user, await SeedRoleAsync());
    }

    private async Task<Role> SeedRoleAsync()
    {
        var role = new Role(new RoleCode($"Role.{Guid.NewGuid():N}"), RoleStatus.Active);
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Roles.Add(role);
        await dbContext.SaveChangesAsync();
        return role;
    }

    private static Task<int> InsertAsync(
        NpgsqlConnection connection, Guid userId, Guid roleId, DateTimeOffset assignedAt, DateTimeOffset? endedAt) =>
        PostgresAssert.ExecuteAsync(connection, null, InsertUserRole,
            ("id", Guid.CreateVersion7()),
            ("user_id", userId),
            ("role_id", roleId),
            ("assigned_at", assignedAt),
            ("ended_at", endedAt.HasValue ? endedAt.Value : DBNull.Value));

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
