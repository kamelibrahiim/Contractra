using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Rolling back SeedAuthorizationCatalog while a user holds a seeded role must
/// fail on the RESTRICT foreign key and lose nothing (ADR 0004 §5; plan E16;
/// TASK-BE-033). Own database: migrates down.
/// </summary>
public class SeedAuthorizationCatalogRollbackTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private const string MigrationBeforeTheSeed = "20260926002226_AddUserPartyLinks";
    private const string SeedMigration = "20260926091750_SeedAuthorizationCatalog";

    private readonly PostgresTestDatabaseFixture _fixture;

    public SeedAuthorizationCatalogRollbackTests(PostgresTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Down_FailsAtomically_WhenASeededRoleIsAssigned_AndEverySeededRowSurvives()
    {
        var at = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        await using (var seedContext = _fixture.CreateDbContext())
        {
            var engineer = (await seedContext.Roles.ToListAsync()).Single(r => r.Code.Value == "Engineer");
            var user = TestUsers.NewUser("Rollback.Engineer");
            user.AssignRole(engineer, at, at);
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync();
        }

        await using var dbContext = _fixture.CreateDbContext();
        var migrator = dbContext.GetService<IMigrator>();

        var exception = await Record.ExceptionAsync(() => migrator.MigrateAsync(MigrationBeforeTheSeed));

        // RESTRICT is reported as 23001 by PostgreSQL 18 and 23503 by earlier
        // versions; either way the foreign key named here refused the delete.
        var postgres = Assert.IsType<PostgresException>(exception);
        Assert.Contains(postgres.SqlState, new[] { "23001", "23503" });
        Assert.Equal("fk_user_roles_roles_role_id", postgres.ConstraintName);

        Assert.DoesNotContain(SeedMigration, await dbContext.Database.GetPendingMigrationsAsync());
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(4, await CountAsync(connection, "SELECT COUNT(*) FROM roles;"));
        Assert.Equal(6, await CountAsync(connection, "SELECT COUNT(*) FROM permissions;"));
        Assert.Equal(7, await CountAsync(connection, "SELECT COUNT(*) FROM role_permissions;"));
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM user_roles;"));

        // The migrations after the seed were rolled back before the failure; the
        // database goes forward again without loss.
        await migrator.MigrateAsync();
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        Assert.Equal(1, await CountAsync(connection, "SELECT COUNT(*) FROM users WHERE login_identifier = 'Rollback.Engineer';"));
    }

    private static Task<long> CountAsync(NpgsqlConnection connection, string sql) =>
        PostgresAssert.CountAsync(connection, null, sql);
}
