using Contractra.Application.Identity.Auth;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.Persistence.Throttling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>Shared plumbing for the Slice 6 Infrastructure tests against an isolated PostgreSQL test database.</summary>
internal static class Slice6Support
{
    public static ServiceProvider BuildProvider(
        PostgresTestDatabaseFixture fixture,
        AuthThrottleOptions? throttle = null,
        CapturingLoggerProvider? logs = null,
        Action<IServiceCollection>? configure = null)
    {
        var configuration = new InMemoryConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString,
            ["Business:TimeZone"] = "Africa/Cairo",
        });
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddInfrastructure(configuration);
        services.AddAuthInfrastructure(throttle ?? new AuthThrottleOptions { LockTimeout = TimeSpan.FromSeconds(2) }, AuthSessionPolicy.Default);
        if (logs is not null)
        {
            services.AddLogging(builder => Microsoft.Extensions.Logging.LoggingBuilderExtensions.AddProvider(builder, logs));
        }

        configure?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    public static async Task<User> SeedUserAsync(PostgresTestDatabaseFixture fixture, DateTimeOffset now, string[] roles, string[]? direct = null)
    {
        await using var db = fixture.CreateDbContext();
        var user = TestUsers.NewUser($"S6.{Guid.NewGuid():N}");
        foreach (var code in roles)
        {
            user.AssignRole(await db.Roles.SingleAsync(r => r.Code == new RoleCode(code)), now.AddDays(-1), now);
        }

        foreach (var code in direct ?? [])
        {
            user.GrantPermission(await db.Permissions.SingleAsync(p => p.Code == new PermissionCode(code)), now.AddDays(-1), now);
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
