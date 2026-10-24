using System.Security.Cryptography;
using Contractra.Api.Auth;
using Contractra.Application.Common;
using Contractra.Infrastructure;
using Contractra.Infrastructure.IntegrationTests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contractra.Api.Tests.Support;

/// <summary>
/// A test host composed exactly like Program's web branch (the same Serilog, Infrastructure
/// and auth pipeline extension methods and the real auth endpoints), plus test-only
/// endpoints for the audiences that have no real endpoint in Slice 6: <c>admin</c>,
/// <c>employee</c>, an any-session endpoint that is not on the §7 list, and one that
/// declares no audience at all.
/// </summary>
public sealed class GateHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private GateHost(WebApplication app, TestClock clock, CapturingLoggerProvider logs, byte[] key, string kid)
    {
        _app = app;
        Clock = clock;
        Logs = logs;
        Key = key;
        KeyId = kid;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public TestClock Clock { get; }

    public CapturingLoggerProvider Logs { get; }

    public byte[] Key { get; }

    public string KeyId { get; }

    public static async Task<GateHost> StartAsync(PostgresTestDatabaseFixture database, Action<IServiceCollection>? configureServices = null)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        const string kid = "gate-key";
        var clock = new TestClock();
        var logs = new CapturingLoggerProvider();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
            ["Business:TimeZone"] = "Africa/Cairo",
            ["Auth:Jwt:ActiveKeyId"] = kid,
            [$"Auth:Jwt:SigningKeys:{kid}"] = Convert.ToBase64String(key),
            ["Auth:Throttle:PublicPermitLimit"] = "100000",
            ["Auth:Throttle:RefreshPermitLimit"] = "100000",
            ["Auth:Throttle:CleanupEnabled"] = "false",
        });

        builder.AddContractraSerilog();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.AddContractraAuthHttpPipeline();
        builder.Services.RemoveAll<IClock>();
        builder.Services.AddSingleton<IClock>(clock);
        builder.Services.AddSingleton<ILoggerProvider>(logs);
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseContractraAuthHttpPipeline();
        app.MapAuthEndpoints();

        app.MapGet("/test/admin", () => Results.Ok("admin")).WithAudience(AuthAudience.Admin);
        app.MapGet("/test/admin/users", () => Results.Ok("users"))
            .WithAudience(AuthAudience.Admin)
            .WithMetadata(new RequiredPermissionMetadata("Users.Manage"));
        app.MapGet("/test/admin/payroll", () => Results.Ok("payroll"))
            .WithAudience(AuthAudience.Admin)
            .WithMetadata(new RequiredPermissionMetadata("Payroll.View"));
        app.MapGet("/test/employee", () => Results.Ok("employee")).WithAudience(AuthAudience.Employee);
        app.MapGet("/test/any", () => Results.Ok("any")).WithAudience(AuthAudience.AnySession);
        app.MapGet("/test/undeclared", () => Results.Ok("undeclared"));

        await app.StartAsync();
        return new GateHost(app, clock, logs, key, kid);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
