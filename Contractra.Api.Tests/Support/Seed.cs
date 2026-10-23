using Contractra.Domain.Identity;
using Contractra.Infrastructure.Identity;
using Contractra.Infrastructure.IntegrationTests;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Api.Tests.Support;

public sealed record SeededUser(Guid Id, string Login, string Password);

/// <summary>Users written straight to the ephemeral database with real password hashes.</summary>
public static class Seed
{
    private static readonly AspNetPasswordHasher Hasher = new();

    public static string NewLogin(string prefix = "user") => $"{prefix}.{Guid.NewGuid():N}";

    /// <summary>Random lowercase letters (never digits, so it can never look like a phone number to the username policy).</summary>
    public static string NewLetters(int count) =>
        new(Enumerable.Range(0, count).Select(_ => (char)('a' + System.Security.Cryptography.RandomNumberGenerator.GetInt32(26))).ToArray());

    /// <summary>A login that passes username policy v1: lowercase, 3-32 characters, letters and one separator.</summary>
    public static string NewPolicyLogin(string prefix = "user") => $"{prefix.ToLowerInvariant()}.{NewLetters(10)}";

    public static async Task<SeededUser> UserAsync(
        PostgresTestDatabaseFixture database,
        DateTimeOffset now,
        string[] roles,
        string? login = null,
        string password = "Correct-Passw0rd",
        bool mustChangePassword = false,
        DateTimeOffset? temporaryPasswordExpiresAt = null,
        bool disabled = false,
        string[]? directPermissions = null,
        string? passwordHash = null)
    {
        login ??= NewLogin();
        await using var db = database.CreateDbContext();
        var since = now.AddDays(-1);
        var user = new User(
            new LoginIdentifier(login),
            UserStatus.Active,
            passwordHash ?? Hasher.Hash(password),
            mustChangePassword,
            mustChangePassword ? temporaryPasswordExpiresAt ?? now.AddHours(72) : null);

        foreach (var code in roles)
        {
            var role = await db.Roles.SingleAsync(r => r.Code == new RoleCode(code));
            user.AssignRole(role, since, now);
        }

        foreach (var code in directPermissions ?? [])
        {
            var permission = await db.Permissions.SingleAsync(p => p.Code == new PermissionCode(code));
            user.GrantPermission(permission, since, now);
        }

        if (disabled)
        {
            user.Disable();
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new SeededUser(user.Id, login, password);
    }

    public static async Task<SeededUser> BootstrapAdminAsync(PostgresTestDatabaseFixture database, DateTimeOffset now, string password = "Bootstrap-Passw0rd")
    {
        await using var db = database.CreateDbContext();
        var adminRole = await db.Roles.SingleAsync(r => r.Code == new RoleCode("Admin"));
        var payrollView = await db.Permissions.SingleAsync(p => p.Code == new PermissionCode(PermissionCode.PayrollView));
        var login = NewLogin("boot");
        var admin = User.CreateBootstrapAdmin(new LoginIdentifier(login), Hasher.Hash(password), adminRole, payrollView, now.AddDays(-1), now);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return new SeededUser(admin.Id, login, password);
    }

    /// <summary>Ends an active role assignment through the aggregate (bumps the security version).</summary>
    public static async Task EndRoleAsync(PostgresTestDatabaseFixture database, Guid userId, string roleCode, DateTimeOffset now)
    {
        await using var db = database.CreateDbContext();
        var user = await db.Users.Include(u => u.Roles).SingleAsync(u => u.Id == userId);
        var role = await db.Roles.SingleAsync(r => r.Code == new RoleCode(roleCode));
        user.EndRole(role, now, now);
        await db.SaveChangesAsync();
    }

    public static async Task AssignRoleAsync(PostgresTestDatabaseFixture database, Guid userId, string roleCode, DateTimeOffset now)
    {
        await using var db = database.CreateDbContext();
        var user = await db.Users.Include(u => u.Roles).SingleAsync(u => u.Id == userId);
        var role = await db.Roles.SingleAsync(r => r.Code == new RoleCode(roleCode));
        user.AssignRole(role, now, now);
        await db.SaveChangesAsync();
    }

    public static async Task<AuthSession> SessionAsync(PostgresTestDatabaseFixture database, Guid sessionId)
    {
        await using var db = database.CreateDbContext();
        return await db.AuthSessions.AsNoTracking().Include(s => s.RefreshTokens).SingleAsync(s => s.Id == sessionId);
    }

    public static async Task<List<AuthSession>> SessionsOfAsync(PostgresTestDatabaseFixture database, Guid userId)
    {
        await using var db = database.CreateDbContext();
        return await db.AuthSessions.AsNoTracking().Include(s => s.RefreshTokens).Where(s => s.UserId == userId).ToListAsync();
    }

    public static async Task<User> UserByIdAsync(PostgresTestDatabaseFixture database, Guid userId)
    {
        await using var db = database.CreateDbContext();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    public static async Task<List<Contractra.Domain.Audit.AuditEvent>> AuditEventsAsync(PostgresTestDatabaseFixture database, Guid? actorUserId = null)
    {
        await using var db = database.CreateDbContext();
        var query = db.AuditEvents.AsNoTracking();
        if (actorUserId is { } actor)
        {
            query = query.Where(e => e.ActorUserId == actor);
        }

        return await query.OrderBy(e => e.Id).ToListAsync();
    }

    /// <summary>Every audit_events row as text (all columns), for secret-leak checks.</summary>
    public static async Task<string> AuditTableTextAsync(PostgresTestDatabaseFixture database)
    {
        await using var db = database.CreateDbContext();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT coalesce(string_agg(t::text, E'\\n'), '') FROM audit_events t;";
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
