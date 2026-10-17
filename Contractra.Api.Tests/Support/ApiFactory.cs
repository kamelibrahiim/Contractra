using System.Net;
using System.Security.Cryptography;
using Contractra.Application.Common;
using Contractra.Infrastructure.IntegrationTests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contractra.Api.Tests.Support;

/// <summary>A settable application clock shared by the use cases, the throttle policy and JWT lifetime validation.</summary>
public sealed class TestClock : IClock
{
    private long _ticks = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).UtcTicks;

    public DateTimeOffset UtcNow => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

/// <summary>
/// The real Program (web host) against an ephemeral PostgreSQL database from
/// <see cref="PostgresTestDatabaseFixture"/> (CONTRACTRA_TEST_POSTGRES only). Signing keys
/// are generated here, per factory, and never written anywhere. Layers 1 and 5 are set
/// high by default so unrelated tests are not rate limited; throttle tests lower them.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string TestRemoteIpHeader = "X-Test-Remote-IP";
    public const string DefaultRemoteIp = "192.0.2.10";

    private readonly PostgresTestDatabaseFixture _database;
    private readonly Dictionary<string, string?> _settings;
    private readonly string _environment;
    private readonly Action<IServiceCollection>? _configureServices;

    public ApiFactory(
        PostgresTestDatabaseFixture database,
        IReadOnlyDictionary<string, string?>? settings = null,
        string environment = "Development",
        Action<IServiceCollection>? configureServices = null)
    {
        _database = database;
        _environment = environment;
        _configureServices = configureServices;
        ActiveKeyId = "test-key-1";
        ActiveKey = RandomNumberGenerator.GetBytes(32);

        _settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
            ["Business:TimeZone"] = "Africa/Cairo",
            ["Auth:Jwt:ActiveKeyId"] = ActiveKeyId,
            [$"Auth:Jwt:SigningKeys:{ActiveKeyId}"] = Convert.ToBase64String(ActiveKey),
            ["Auth:Throttle:PublicPermitLimit"] = "100000",
            ["Auth:Throttle:RefreshPermitLimit"] = "100000",
            ["Auth:Throttle:CleanupEnabled"] = "false",
            ["Auth:Throttle:LockTimeout"] = "00:00:02",
        };

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            _settings[key] = value;
        }
    }

    public string ActiveKeyId { get; }

    public byte[] ActiveKey { get; }

    public TestClock Clock { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public PostgresTestDatabaseFixture Database => _database;

    public HttpClient CreateApiClient(string baseAddress = "http://localhost") =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri(baseAddress),
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        foreach (var (key, value) in _settings)
        {
            if (value is not null)
            {
                builder.UseSetting(key, value);
            }
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
            services.AddSingleton<ILoggerProvider>(Logs);
            services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();
            _configureServices?.Invoke(services);
        });
    }

    /// <summary>Lets a test choose the connection's remote address (the TCP peer), as a real socket would provide it.</summary>
    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                var header = context.Request.Headers[TestRemoteIpHeader].ToString();
                context.Connection.RemoteIpAddress = IPAddress.Parse(string.IsNullOrEmpty(header) ? DefaultRemoteIp : header);
                context.Request.Headers.Remove(TestRemoteIpHeader);
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
