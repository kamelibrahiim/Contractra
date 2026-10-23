using System.Diagnostics;
using Contractra.Api.Auth;
using Contractra.Api.Tests.Support;
using Contractra.Infrastructure.IntegrationTests;
using Contractra.Infrastructure.Persistence.Throttling;

namespace Contractra.Api.Tests;

/// <summary>
/// Program.cs composition: the operator-command branch (contract §10A, §11) still runs and
/// returns before the web host is built - it needs none of the Slice 6 configuration and
/// never listens - and the §17 periodic cleanup deletes only expired, unblocked rows.
/// </summary>
public sealed class CompositionTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _database;

    public CompositionTests(PostgresTestDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task OperatorCommand_RunsWithoutTheWebHost_AndWithoutAnySigningKey()
    {
        var user = await Seed.UserAsync(_database, DateTimeOffset.UtcNow, ["Engineer"]);
        var apiDll = typeof(Program).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(apiDll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(apiDll);
        start.ArgumentList.Add("issue-password-reset");
        start.ArgumentList.Add("--login");
        start.ArgumentList.Add(user.Login);
        start.ArgumentList.Add("--reason");
        start.ArgumentList.Add("Slice 6 composition test");
        // Staging: no user-secrets, no Production fail-closed self-check; and no Auth:Jwt keys at all.
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Staging";
        start.Environment["DOTNET_ENVIRONMENT"] = "Staging";
        start.Environment["ConnectionStrings__DefaultConnection"] = _database.ConnectionString;
        start.Environment["Business__TimeZone"] = "Africa/Cairo";
        start.Environment.Remove("ASPNETCORE_URLS");

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await process.WaitForExitAsync(timeout.Token);
        var output = await stdout + await stderr;

        Assert.True(process.ExitCode == 0, output);
        Assert.Contains("Correlation id:", output, StringComparison.Ordinal);
        Assert.Contains("Reset code (shown once):", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Now listening on", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Auth:Jwt", output, StringComparison.Ordinal);
    }

    // ADR 0007 §10: the fake WhatsApp sender receives plaintext passwords in memory, so it can never be registered
    // outside Development or Testing - startup fails closed instead of silently using it.
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void TheFakeSender_RefusesToStartTheWebHostOutsideDevelopmentAndTesting(string environment)
    {
        using var factory = new ApiFactory(_database, new Dictionary<string, string?> { ["WhatsApp:Sender"] = "Fake" }, environment);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());

        Assert.Contains("fake WhatsApp sender", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ThrottleCleanup_DeletesOnlyExpiredUnblockedRows()
    {
        await using var factory = new ApiFactory(_database);
        _ = factory.CreateApiClient();
        var now = factory.Clock.UtcNow;
        var expired = new AuthThrottleKey(AuthThrottleScopes.LoginPair, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var live = new AuthThrottleKey(AuthThrottleScopes.LoginPair, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var blocked = new AuthThrottleKey(AuthThrottleScopes.Identifier, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<AuthThrottleStore>();
            await store.ApplyAsync([expired, live, blocked], _ => new AuthThrottleDecision<bool>(true, new Dictionary<AuthThrottleKey, AuthThrottleState?>
            {
                [expired] = new(AuthThrottle.ToStoredPrecision(now.AddMinutes(-20)), 3, null),
                [live] = new(AuthThrottle.ToStoredPrecision(now.AddMinutes(-5)), 3, null),
                [blocked] = new(AuthThrottle.ToStoredPrecision(now.AddMinutes(-20)), 10, AuthThrottle.ToStoredPrecision(now.AddMinutes(1))),
            }), TimeSpan.FromSeconds(2));
        }

        var deleted = await factory.Services.GetRequiredService<AuthThrottleCleanupService>().RunOnceAsync(CancellationToken.None);

        Assert.True(deleted >= 1);
        await using var check = factory.Services.CreateAsyncScope();
        var after = check.ServiceProvider.GetRequiredService<AuthThrottleStore>();
        Assert.Null(await after.GetAsync(expired));
        Assert.NotNull(await after.GetAsync(live));
        Assert.NotNull(await after.GetAsync(blocked));
    }
}
