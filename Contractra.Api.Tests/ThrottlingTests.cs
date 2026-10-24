using System.Net;
using Contractra.Api.Tests.Support;
using Contractra.Application.Identity.Operations;
using Contractra.Infrastructure.IntegrationTests;
using Npgsql;

namespace Contractra.Api.Tests;

/// <summary>
/// Contract v1.5 §17 (owner decision R45, E5): layers 2-4 over the shared store, with the
/// application clock; 429 auth.too_many_attempts with Retry-After (§6, §12).
/// </summary>
public sealed class ThrottlingTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private readonly PostgresTestDatabaseFixture _database;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;
    private int _ipCounter;

    public ThrottlingTests(PostgresTestDatabaseFixture database)
    {
        _database = database;
    }

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_database, new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5" });
        _client = _factory.CreateApiClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private TestClock Clock => _factory.Clock;

    private string NewIp() => $"198.18.{Interlocked.Increment(ref _ipCounter) / 250}.{(_ipCounter % 250) + 1}";

    private static void AssertThrottled(ApiResponse response, int retryAfterSeconds)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.Status);
        Assert.Equal("auth.too_many_attempts", response.Code);
        Assert.Equal(retryAfterSeconds.ToString(), response.Header("Retry-After"));
    }

    [Fact]
    public async Task Layer2_FiveFailuresPerPair_Then429_WithADoublingDelay()
    {
        var user = await Seed.UserAsync(_database, Clock.UtcNow, ["Engineer"]);
        var ip = NewIp();
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: ip)).Status);
        }

        // Throttle checks run before the password: even the correct one is refused now.
        AssertThrottled(await Calls.LoginAsync(_client, user.Login, user.Password, ip: ip), 30);
        Clock.Advance(TimeSpan.FromSeconds(10));
        AssertThrottled(await Calls.LoginAsync(_client, user.Login, user.Password, ip: ip), 20);

        Clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: ip)).Status);
        AssertThrottled(await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: ip), 60);
        Clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: ip)).Status);
        AssertThrottled(await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: ip), 120);

        Assert.True(_factory.Logs.Contains("Auth.Throttled Layer=\"2\""), _factory.Logs.AllText);
        // §6, §14: a 429 is Serilog only, never an audit event.
        Assert.Empty(await Seed.AuditEventsAsync(_database, user.Id));
    }

    [Fact]
    public async Task Layer2_DelayIsCappedAt15Minutes()
    {
        var login = Seed.NewLogin("cap");
        var ip = NewIp();
        for (var i = 0; i < 5; i++)
        {
            await Calls.LoginAsync(_client, login, "Wrong-1", ip: ip);
        }

        // 30, 60, 120, 240, 480, then 900 (capped).
        foreach (var delay in new[] { 30, 60, 120, 240, 480, 900, 900 })
        {
            Clock.Advance(TimeSpan.FromSeconds(delay));
            Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.LoginAsync(_client, login, "Wrong-1", ip: ip)).Status);
            var next = delay == 900 ? 900 : Math.Min(delay * 2, 900);
            AssertThrottled(await Calls.LoginAsync(_client, login, "Wrong-1", ip: ip), next);
        }
    }

    [Fact]
    public async Task Layer2_ASuccessClearsThePair()
    {
        var user = await Seed.UserAsync(_database, Clock.UtcNow, ["Engineer"]);
        var ip = NewIp();
        for (var i = 0; i < 4; i++)
        {
            await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: ip);
        }

        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, user.Login, user.Password, ip: ip)).Status);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: ip)).Status);
        }

        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, user.Login, user.Password, ip: ip)).Status);
    }

    [Fact]
    public async Task Layer2_AnotherAddressIsNotBlockedByThePair()
    {
        var user = await Seed.UserAsync(_database, Clock.UtcNow, ["Engineer"]);
        var attacker = NewIp();
        for (var i = 0; i < 5; i++)
        {
            await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: attacker);
        }

        AssertThrottled(await Calls.LoginAsync(_client, user.Login, user.Password, ip: attacker), 30);
        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, user.Login, user.Password, ip: NewIp())).Status);
    }

    [Fact]
    public async Task Layer2_WindowPassesWithoutReachingTheLimit_StartsAFreshWindow()
    {
        var user = await Seed.UserAsync(_database, Clock.UtcNow, ["Engineer"]);
        var ip = NewIp();
        for (var i = 0; i < 4; i++)
        {
            await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: ip);
        }

        Clock.Advance(TimeSpan.FromMinutes(15));
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: ip)).Status);
        }

        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, user.Login, user.Password, ip: ip)).Status);
    }

    [Fact]
    public async Task UnknownAndKnownIdentifiers_AreThrottledIdentically()
    {
        var known = await Seed.UserAsync(_database, Clock.UtcNow, ["Engineer"]);
        var unknown = Seed.NewLogin("ghost");
        var ipKnown = NewIp();
        var ipUnknown = NewIp();
        for (var i = 0; i < 5; i++)
        {
            await Calls.LoginAsync(_client, known.Login, "Wrong-1", ip: ipKnown);
            await Calls.LoginAsync(_client, unknown, "Wrong-1", ip: ipUnknown);
        }

        var a = await Calls.LoginAsync(_client, known.Login, "Wrong-1", ip: ipKnown);
        var b = await Calls.LoginAsync(_client, unknown, "Wrong-1", ip: ipUnknown);
        AssertThrottled(a, 30);
        AssertThrottled(b, 30);
        Assert.Equal(
            string.Join(",", a.Json.EnumerateObject().Where(p => p.Name != "correlationId").Select(p => p.ToString())),
            string.Join(",", b.Json.EnumerateObject().Where(p => p.Name != "correlationId").Select(p => p.ToString())));
    }

    [Fact]
    public async Task LoginAndResetCompletion_ShareTheCounters()
    {
        var user = await Seed.UserAsync(_database, Clock.UtcNow, ["Engineer"]);
        var ip = NewIp();
        for (var i = 0; i < 3; i++)
        {
            await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: ip);
        }

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal("auth.reset_code_invalid", (await Calls.CompleteResetAsync(_client, user.Login, "AAAA-BBBB-CCCC-DDDD", "Chosen-Passw0rd", ip)).Code);
        }

        AssertThrottled(await Calls.LoginAsync(_client, user.Login, user.Password, ip: ip), 30);
        AssertThrottled(await Calls.CompleteResetAsync(_client, user.Login, "AAAA-BBBB-CCCC-DDDD", "Chosen-Passw0rd", ip), 30);
    }

    [Fact]
    public async Task ResetCompletion_SuccessClearsThePair()
    {
        var user = await Seed.UserAsync(_database, Clock.UtcNow, ["Engineer"]);
        var ip = NewIp();
        string code;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            code = (await scope.ServiceProvider.GetRequiredService<OperatorIssuePasswordResetUseCase>()
                .ExecuteAsync(new OperatorIssuePasswordResetRequest(user.Login, "throttle test", Guid.NewGuid().ToString("D")))).Value!.Code;
        }

        for (var i = 0; i < 4; i++)
        {
            await Calls.CompleteResetAsync(_client, user.Login, "AAAA-BBBB-CCCC-DDDD", "Chosen-Passw0rd", ip);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await Calls.CompleteResetAsync(_client, user.Login, code, "Chosen-Passw0rd", ip)).Status);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: ip)).Status);
        }
    }

    [Fact]
    public async Task Layer3_TenFailuresFromManyAddresses_SlowTheIdentifierToOneAttemptPer30Seconds_WithoutBlockingIt()
    {
        var user = await Seed.UserAsync(_database, Clock.UtcNow, ["Engineer"]);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: NewIp())).Status);
        }

        Assert.Contains(_factory.Logs.Entries, e => e.Level == LogLevel.Warning && e.Text.Contains("Activated=True", StringComparison.OrdinalIgnoreCase) && e.Text.Contains("\"3\"", StringComparison.Ordinal));

        // From a fresh address, immediately: the identifier's one slot is taken.
        AssertThrottled(await Calls.LoginAsync(_client, user.Login, user.Password, ip: NewIp()), 30);

        // The real user waits at most 30 seconds, then gets in (slowed, never locked out).
        Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, user.Login, user.Password, ip: NewIp())).Status);

        // A success does not end slow mode: it took the slot.
        AssertThrottled(await Calls.LoginAsync(_client, user.Login, user.Password, ip: NewIp()), 30);

        // Slow mode ends once more than 15 minutes pass without an attempt.
        Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: NewIp())).Status);
        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, user.Login, user.Password, ip: NewIp())).Status);
    }

    [Fact]
    public async Task Layer3_RejectedAttemptsDoNotTakeTheSlot()
    {
        var user = await Seed.UserAsync(_database, Clock.UtcNow, ["Engineer"]);
        for (var i = 0; i < 10; i++)
        {
            await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: NewIp());
        }

        for (var i = 0; i < 5; i++)
        {
            Clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.TooManyRequests, (await Calls.LoginAsync(_client, user.Login, "Wrong-1", ip: NewIp())).Status);
        }

        Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, user.Login, user.Password, ip: NewIp())).Status);
    }

    [Fact]
    public async Task Layer4_FiveWrongCurrentPasswords_Then429_TheSessionStaysValid()
    {
        var user = await Seed.UserAsync(_database, Clock.UtcNow, ["Engineer"]);
        var token = (await Calls.LoginAsync(_client, user.Login, user.Password)).String("accessToken")!;

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal("auth.current_password_incorrect", (await Calls.ChangePasswordAsync(_client, token, "Wrong-Current", "Brand-New-Passw0rd")).Code);
        }

        AssertThrottled(await Calls.ChangePasswordAsync(_client, token, user.Password, "Brand-New-Passw0rd"), 900);
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(_client, token)).Status);
        Assert.True(_factory.Logs.Contains("Auth.Throttled Layer=\"4\""));

        Clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(HttpStatusCode.OK, (await Calls.ChangePasswordAsync(_client, token, user.Password, "Brand-New-Passw0rd")).Status);
    }

    [Fact]
    public async Task Layer4_OnlyWrongCurrentPasswordsCount()
    {
        var user = await Seed.UserAsync(_database, Clock.UtcNow, ["Engineer"]);
        var token = (await Calls.LoginAsync(_client, user.Login, user.Password)).String("accessToken")!;

        for (var i = 0; i < 4; i++)
        {
            await Calls.ChangePasswordAsync(_client, token, "Wrong-Current", "Brand-New-Passw0rd");
        }

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal("auth.password_too_short", (await Calls.ChangePasswordAsync(_client, token, user.Password, "short")).Code);
        }

        Assert.Equal("auth.current_password_incorrect", (await Calls.ChangePasswordAsync(_client, token, "Wrong-Current", "Brand-New-Passw0rd")).Code);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Calls.ChangePasswordAsync(_client, token, user.Password, "Brand-New-Passw0rd")).Status);
    }

    [Fact]
    public async Task TrustedProxy_ForwardedForIsTheClient_FromAnyOtherPeerItIsIgnored()
    {
        var user = await Seed.UserAsync(_database, Clock.UtcNow, ["Engineer"]);
        async Task<ApiResponse> ViaAsync(string peer, string forwardedFor, string password) =>
            await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/login",
                new { loginIdentifier = user.Login, password, clientApp = "employee" },
                headers: new Dictionary<string, string> { [ApiFactory.TestRemoteIpHeader] = peer, ["X-Forwarded-For"] = forwardedFor });

        // Through the trusted proxy: X-Forwarded-For is the client.
        for (var i = 0; i < 5; i++)
        {
            await ViaAsync("10.0.0.5", "203.0.113.7", "Wrong-1");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await ViaAsync("10.0.0.5", "203.0.113.7", user.Password)).Status);
        Assert.Equal(HttpStatusCode.OK, (await ViaAsync("10.0.0.5", "203.0.113.8", user.Password)).Status);
        Assert.True(_factory.Logs.Contains("203.0.113.7"));

        // From an untrusted peer, a spoofed X-Forwarded-For changes nothing: one pair.
        var untrusted = NewIp();
        for (var i = 0; i < 5; i++)
        {
            await ViaAsync(untrusted, $"192.0.2.{100 + i}", "Wrong-1");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await ViaAsync(untrusted, "192.0.2.200", user.Password)).Status);
    }

    [Fact]
    public async Task ThrottleLogs_NeverContainTheRawIdentifier()
    {
        var login = Seed.NewLogin("Secret.Name");
        var ip = NewIp();
        for (var i = 0; i < 6; i++)
        {
            await Calls.LoginAsync(_client, login, "Wrong-1", ip: ip);
        }

        Assert.True(_factory.Logs.Contains("Auth.Throttled"));
        Assert.DoesNotContain(login, _factory.Logs.AllText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Contractra.Application.Identity.Auth.ThrottleKeys.ToHex(Contractra.Application.Identity.Auth.ThrottleKeys.IdentifierKey(login)), _factory.Logs.AllText);
    }

    [Fact]
    public async Task SharedStoreUnavailable_LoginAndResetCompletionFailClosedWith503()
    {
        var user = await Seed.UserAsync(_database, Clock.UtcNow, ["Engineer"]);
        var token = (await Calls.LoginAsync(_client, user.Login, user.Password)).String("accessToken")!;

        await using (var blocker = new NpgsqlConnection(_database.ConnectionString))
        {
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using (var lockTable = new NpgsqlCommand("LOCK TABLE auth_throttle_counters IN ACCESS EXCLUSIVE MODE;", blocker, transaction))
            {
                await lockTable.ExecuteNonQueryAsync();
            }

            var login = await Calls.LoginAsync(_client, user.Login, user.Password, ip: NewIp());
            var reset = await Calls.CompleteResetAsync(_client, user.Login, "AAAA-BBBB-CCCC-DDDD", "Chosen-Passw0rd", NewIp());
            var change = await Calls.ChangePasswordAsync(_client, token, user.Password, "Brand-New-Passw0rd");

            foreach (var response in new[] { login, reset, change })
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
                Assert.Equal("server.error", response.Code);
            }

            Assert.True(_factory.Logs.Contains("Auth.ThrottleStoreUnavailable"));
            await transaction.RollbackAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, user.Login, user.Password, ip: NewIp())).Status);
    }

    [Fact]
    public void Startup_RefusesToTrustEveryAddress()
    {
        using var factory = new ApiFactory(_database, new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks:0"] = "0.0.0.0/0" });
        Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());
    }
}

/// <summary>Contract v1.5 §17 layers 1 and 5: the per-instance in-memory rate limiter.</summary>
public sealed class RateLimitLayerTests : IClassFixture<PostgresTestDatabaseFixture>
{
    private readonly PostgresTestDatabaseFixture _database;

    public RateLimitLayerTests(PostgresTestDatabaseFixture database)
    {
        _database = database;
    }

    private static Dictionary<string, string> From(string ip) => new() { [ApiFactory.TestRemoteIpHeader] = ip };

    [Fact]
    public async Task Layer1_30RequestsPerMinutePerIp_AcrossAllPublicEndpoints()
    {
        await using var factory = new ApiFactory(_database, new Dictionary<string, string?>
        {
            ["Auth:Throttle:PublicPermitLimit"] = null,   // the contract default, 30
            ["Auth:Throttle:PublicWindow"] = "00:10:00",  // no window roll-over during the test
        });
        var client = factory.CreateApiClient();
        const string ip = "198.51.100.30";

        for (var i = 0; i < 10; i++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}", headers: From(ip))).Status);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/refresh", headers: From(ip))).Status);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/password-reset/complete", rawBody: "{}", headers: From(ip))).Status);
        }

        var throttled = await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}", headers: From(ip));
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.Status);
        Assert.Equal("auth.too_many_attempts", throttled.Code);
        Assert.True(int.Parse(throttled.Header("Retry-After")!) > 0);
        Assert.True(factory.Logs.Contains("Auth.Throttled Layer=\"1\""));

        // Another address, and authenticated endpoints, are unaffected.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}", headers: From("198.51.100.31"))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.SendAsync(client, HttpMethod.Get, "/api/v1/auth/me", headers: From(ip))).Status);
    }

    [Fact]
    public async Task Layer1_ResetsWhenTheWindowPasses()
    {
        await using var factory = new ApiFactory(_database, new Dictionary<string, string?>
        {
            ["Auth:Throttle:PublicPermitLimit"] = "2",
            ["Auth:Throttle:PublicWindow"] = "00:00:01",
        });
        var client = factory.CreateApiClient();
        const string ip = "198.51.100.40";

        await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}", headers: From(ip));
        await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}", headers: From(ip));
        var limited = await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}", headers: From(ip));
        if (limited.Status != HttpStatusCode.TooManyRequests)
        {
            // The one-second window rolled over between the requests; take one more shot.
            await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}", headers: From(ip));
            limited = await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}", headers: From(ip));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Status);
        await Task.Delay(TimeSpan.FromMilliseconds(2100));
        Assert.Equal(HttpStatusCode.BadRequest, (await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}", headers: From(ip))).Status);
    }

    [Fact]
    public async Task Layer5_60RefreshesPerMinutePerIp()
    {
        await using var factory = new ApiFactory(_database, new Dictionary<string, string?>
        {
            ["Auth:Throttle:RefreshPermitLimit"] = null, // the contract default, 60
            ["Auth:Throttle:RefreshWindow"] = "00:10:00",
        });
        var client = factory.CreateApiClient();
        const string ip = "198.51.100.50";

        for (var i = 0; i < 60; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/refresh", headers: From(ip))).Status);
        }

        var throttled = await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/refresh", headers: From(ip));
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.Status);
        Assert.Equal("auth.too_many_attempts", throttled.Code);
        Assert.NotNull(throttled.Header("Retry-After"));
        Assert.True(factory.Logs.Contains("Auth.Throttled Layer=\"5\""));

        // Layer 5 is refresh only.
        Assert.Equal(HttpStatusCode.BadRequest, (await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}", headers: From(ip))).Status);
    }
}
