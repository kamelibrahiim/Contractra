using System.Text.RegularExpressions;
using Contractra.Application.Audit;
using Contractra.Application.Identity.Auth;
using Contractra.Domain.Audit;
using Contractra.Infrastructure.Persistence;
using Contractra.Infrastructure.Persistence.Audit;
using Contractra.Infrastructure.Persistence.Throttling;
using Contractra.Infrastructure.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>
/// Shared plumbing for the Slice 5 tests (bootstrap seeder, startup self-check,
/// operator commands) against an isolated PostgreSQL test database.
/// </summary>
internal static class Slice5Support
{
    public static ServiceProvider BuildProvider(
        PostgresTestDatabaseFixture fixture,
        bool failClosedOnUnsafeAuditPrivileges = false,
        IReadOnlyDictionary<string, string?>? settings = null,
        CapturingLoggerProvider? logs = null,
        bool failingAuditWriter = false,
        bool withAuthThrottle = false)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString,
            ["Business:TimeZone"] = "Africa/Cairo",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var configuration = new InMemoryConfiguration(values);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration);
        services.AddStartupInitialization(failClosedOnUnsafeAuditPrivileges);
        if (withAuthThrottle)
        {
            // Only for tests that seed or inspect throttle state themselves (IAuthThrottle,
            // AuthThrottleStore); the operator-command host itself never registers these.
            services.AddAuthInfrastructure(new AuthThrottleOptions { LockTimeout = TimeSpan.FromSeconds(2) }, AuthSessionPolicy.Default);
        }

        if (logs is not null)
        {
            services.AddLogging(builder => builder.AddProvider(logs));
        }

        if (failingAuditWriter)
        {
            services.AddScoped<IAuditWriter>(sp =>
                new BadActorAuditWriter(new AuditWriter(sp.GetRequiredService<ContractraDbContext>())));
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    public static async Task<(int ExitCode, string Output, string Error)> RunCommandAsync(ServiceProvider provider, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await provider.GetRequiredService<OperatorCommandRunner>().RunAsync(args, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    public static string CorrelationIdIn(string output) =>
        Regex.Match(output, @"Correlation id: (?<id>\S+)").Groups["id"].Value;

    public static string ResetCodeIn(string output) =>
        Regex.Match(output, @"Reset code \(shown once\): (?<code>\S+)").Groups["code"].Value;

    public static string NewPasswordIn(string output) =>
        Regex.Match(output, @"New bootstrap admin password \(shown once\): (?<password>\S+)").Groups["password"].Value;
}

/// <summary>
/// Stages the real event, then one more valid-looking event whose actor matches no
/// user, so the database refuses the use case's final save (SQLSTATE 23503) after
/// any earlier save inside the same transaction.
/// </summary>
internal sealed class BadActorAuditWriter : IAuditWriter
{
    private readonly IAuditWriter _inner;

    public BadActorAuditWriter(IAuditWriter inner) => _inner = inner;

    public void Stage(AuditEvent auditEvent)
    {
        _inner.Stage(auditEvent);
        _inner.Stage(AuditEvent.ForUser(
            AuditActionType.PasswordChanged, Guid.CreateVersion7(), actorRoleCode: null, "User", Guid.CreateVersion7(),
            AuditSql.T0, "corr-failing", AuditSource.Web));
    }
}

/// <summary>A dictionary-backed IConfiguration, so the tests need no configuration package.</summary>
internal sealed class InMemoryConfiguration : IConfiguration
{
    private readonly Dictionary<string, string?> _values;

    public InMemoryConfiguration(Dictionary<string, string?> values) => _values = values;

    public string? this[string key]
    {
        get => _values.GetValueOrDefault(key);
        set => throw new NotSupportedException();
    }

    public IConfigurationSection GetSection(string key) => new Section(this, key);

    public IEnumerable<IConfigurationSection> GetChildren() => [];

    public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

    private sealed class Section : IConfigurationSection
    {
        private readonly InMemoryConfiguration _root;

        public Section(InMemoryConfiguration root, string path) => (_root, Path) = (root, path);

        public string Path { get; }

        public string Key => Path.Split(':').Last();

        public string? Value
        {
            get => _root[Path];
            set => throw new NotSupportedException();
        }

        public string? this[string key]
        {
            get => _root[$"{Path}:{key}"];
            set => throw new NotSupportedException();
        }

        public IConfigurationSection GetSection(string key) => new Section(_root, $"{Path}:{key}");

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => _root.GetReloadToken();
    }
}

/// <summary>Collects every rendered log line (message plus structured arguments) so a test can prove a secret never reached a logger.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<(LogLevel Level, string Category, string Text)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly CapturingLoggerProvider _owner;
        private readonly string _category;

        public CapturingLogger(CapturingLoggerProvider owner, string category) => (_owner, _category) = (owner, category);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                text += " | " + string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}"));
            }

            if (exception is not null)
            {
                text += " | " + exception;
            }

            lock (_owner.Entries)
            {
                _owner.Entries.Add((logLevel, _category, text));
            }
        }
    }
}
