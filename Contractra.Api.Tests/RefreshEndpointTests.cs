using System.Net;
using Contractra.Api.Tests.Support;
using Contractra.Application.Identity.UseCases;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.IntegrationTests;

namespace Contractra.Api.Tests;

/// <summary>POST /api/v1/auth/refresh - contract v1.5 §3.3 (rotation, reuse grace), §3.4 (cookie), §3.6 (idle, 7 days), §4, §5.2, §8 (CSRF).</summary>
public sealed class RefreshEndpointTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private readonly PostgresTestDatabaseFixture _database;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public RefreshEndpointTests(PostgresTestDatabaseFixture database)
    {
        _database = database;
    }

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_database);
        _client = _factory.CreateApiClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private TestClock Clock => _factory.Clock;

    private async Task<(SeededUser User, ApiResponse Login)> SignInAsync(string[]? roles = null, string app = "employee", string? platform = null, bool mustChange = false)
    {
        var user = await Seed.UserAsync(_database, Clock.UtcNow, roles ?? ["Engineer"], mustChangePassword: mustChange);
        var login = await Calls.LoginAsync(_client, user.Login, user.Password, app, platform);
        Assert.Equal(HttpStatusCode.OK, login.Status);
        return (user, login);
    }

    [Fact]
    public async Task Mobile_Rotates_TheTokenOnEveryUse()
    {
        var (user, login) = await SignInAsync();
        var first = login.String("refreshToken")!;
        Clock.Advance(TimeSpan.FromMinutes(5));

        var refreshed = await Calls.RefreshMobileAsync(_client, first);

        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        var second = refreshed.String("refreshToken")!;
        Assert.NotEqual(first, second);
        Assert.Equal("no-store", refreshed.Header("Cache-Control"));
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(_client, refreshed.String("accessToken")!)).Status);

        var session = Assert.Single(await Seed.SessionsOfAsync(_database, user.Id));
        Assert.Equal(Clock.UtcNow, session.LastRefreshedAt);
        Assert.Equal(2, session.RefreshTokens.Count);
        Assert.Single(session.RefreshTokens, t => t.ConsumedAt is null);
        Assert.Equal(HttpStatusCode.OK, (await Calls.RefreshMobileAsync(_client, second)).Status);
    }

    [Fact]
    public async Task Web_RotatesThroughTheCookie_WithTheCsrfHeader_AndNeverExtendsTheExpiry()
    {
        var (user, login) = await SignInAsync(["Admin"], "admin", "web");
        var cookie = Calls.RefreshCookieValue(login)!;
        var loginExpires = login.SetCookies.Single().Split(';').Single(p => p.Trim().StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        Clock.Advance(TimeSpan.FromMinutes(10));

        var refreshed = await Calls.RefreshWebAsync(_client, cookie);

        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        Assert.False(refreshed.Json.TryGetProperty("refreshToken", out _));
        var next = Calls.RefreshCookieValue(refreshed);
        Assert.False(string.IsNullOrEmpty(next));
        Assert.NotEqual(cookie, next);
        var refreshedExpires = refreshed.SetCookies.Single().Split(';').Single(p => p.Trim().StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(loginExpires.Trim(), refreshedExpires.Trim());
    }

    [Fact]
    public async Task Web_WithoutTheCsrfHeader_Is403Forbidden_SerilogOnly_AndConsumesNothing()
    {
        var (user, login) = await SignInAsync(["Admin"], "admin", "web");
        var cookie = Calls.RefreshCookieValue(login)!;

        var rejected = await Calls.RefreshWebAsync(_client, cookie, csrfHeader: false);

        Assert.Equal(HttpStatusCode.Forbidden, rejected.Status);
        Assert.Equal("auth.forbidden", rejected.Code);
        Assert.DoesNotContain(await Seed.AuditEventsAsync(_database), e => e.CorrelationId == rejected.String("correlationId"));
        Assert.Empty(await Seed.AuditEventsAsync(_database, user.Id));
        Assert.True(_factory.Logs.Contains("Auth.CsrfRejected"));
        var session = Assert.Single(await Seed.SessionsOfAsync(_database, user.Id));
        Assert.All(session.RefreshTokens, t => Assert.Null(t.ConsumedAt));

        var wrongValue = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/refresh", headers: new Dictionary<string, string>
        {
            ["Cookie"] = $"contractra_refresh={cookie}",
            ["X-CSRF-Protection"] = "yes",
        });
        Assert.Equal(HttpStatusCode.Forbidden, wrongValue.Status);
        Assert.Equal(HttpStatusCode.OK, (await Calls.RefreshWebAsync(_client, cookie)).Status);
    }

    [Fact]
    public async Task ReuseWithinTheGrace_ReturnsANewAccessTokenOnly_AndKeepsTheSession()
    {
        var (user, login) = await SignInAsync();
        var first = login.String("refreshToken")!;
        var rotated = await Calls.RefreshMobileAsync(_client, first);
        Assert.Equal(HttpStatusCode.OK, rotated.Status);
        Clock.Advance(TimeSpan.FromSeconds(30));

        var reused = await Calls.RefreshMobileAsync(_client, first);

        Assert.Equal(HttpStatusCode.OK, reused.Status);
        Assert.False(reused.Json.TryGetProperty("refreshToken", out _));
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(_client, reused.String("accessToken")!)).Status);
        var session = Assert.Single(await Seed.SessionsOfAsync(_database, user.Id));
        Assert.Null(session.RevokedAt);
        Assert.Equal(2, session.RefreshTokens.Count);
        Assert.Equal(HttpStatusCode.OK, (await Calls.RefreshMobileAsync(_client, rotated.String("refreshToken")!)).Status);
    }

    [Fact]
    public async Task Web_ReuseWithinTheGrace_SetsNoCookie()
    {
        var (_, login) = await SignInAsync(["Admin"], "admin", "web");
        var first = Calls.RefreshCookieValue(login)!;
        Assert.Equal(HttpStatusCode.OK, (await Calls.RefreshWebAsync(_client, first)).Status);
        Clock.Advance(TimeSpan.FromSeconds(10));

        var reused = await Calls.RefreshWebAsync(_client, first);

        Assert.Equal(HttpStatusCode.OK, reused.Status);
        Assert.Empty(reused.SetCookies);
    }

    [Fact]
    public async Task ReuseAfterTheGrace_IsTheft_RevokesTheWholeSession()
    {
        var (user, login) = await SignInAsync();
        var first = login.String("refreshToken")!;
        var rotated = await Calls.RefreshMobileAsync(_client, first);
        Clock.Advance(TimeSpan.FromSeconds(31));

        var reused = await Calls.RefreshMobileAsync(_client, first);

        Assert.Equal(HttpStatusCode.Unauthorized, reused.Status);
        Assert.Equal("auth.refresh_invalid", reused.Code);
        var session = Assert.Single(await Seed.SessionsOfAsync(_database, user.Id));
        Assert.Equal(SessionRevokeReason.RefreshReuse, session.RevokeReason);
        var legitimate = await Calls.RefreshMobileAsync(_client, rotated.String("refreshToken")!);
        Assert.Equal(HttpStatusCode.Unauthorized, legitimate.Status);
        Assert.Equal("auth.refresh_invalid", legitimate.Code);
    }

    [Fact]
    public async Task UnknownOrMissingToken_Is401RefreshInvalid()
    {
        var unknown = await Calls.RefreshMobileAsync(_client, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Status);
        Assert.Equal("auth.refresh_invalid", unknown.Code);

        var none = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/refresh");
        Assert.Equal(HttpStatusCode.Unauthorized, none.Status);
        Assert.Equal("auth.refresh_invalid", none.Code);

        // §14: every refresh failure is logged to Serilog, with its real reason.
        Assert.True(_factory.Logs.Contains("Auth.RefreshFailed"));
        Assert.True(_factory.Logs.Contains("unknown_token"));
        Assert.True(_factory.Logs.Contains("no_token"));
    }

    [Fact]
    public async Task MalformedBody_Is400ValidationFailed()
    {
        var response = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/refresh", rawBody: "{\"refreshToken\": 12}");
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
    }

    [Fact]
    public async Task LoggedOutSession_CannotRefresh()
    {
        var (_, login) = await SignInAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await Calls.LogoutAsync(_client, login.String("accessToken")!)).Status);

        var refreshed = await Calls.RefreshMobileAsync(_client, login.String("refreshToken")!);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.Status);
        Assert.Equal("auth.refresh_invalid", refreshed.Code);
    }

    [Fact]
    public async Task Idle_Engineer8Hours_ExpiresTheSession_AndRevokesItAsIdle()
    {
        var (user, login) = await SignInAsync();
        Clock.Advance(TimeSpan.FromHours(8) - TimeSpan.FromSeconds(1));
        var stillActive = await Calls.RefreshMobileAsync(_client, login.String("refreshToken")!);
        Assert.Equal(HttpStatusCode.OK, stillActive.Status);

        Clock.Advance(TimeSpan.FromHours(8) + TimeSpan.FromSeconds(1));
        var idle = await Calls.RefreshMobileAsync(_client, stillActive.String("refreshToken")!);

        Assert.Equal(HttpStatusCode.Unauthorized, idle.Status);
        Assert.Equal("auth.session_expired", idle.Code);
        var session = Assert.Single(await Seed.SessionsOfAsync(_database, user.Id));
        Assert.Equal(SessionRevokeReason.Idle, session.RevokeReason);
    }

    [Fact]
    public async Task Idle_TheShortestTimeoutAmongActiveRolesApplies()
    {
        // Engineer (8 h) + OfficeStaff (60 min) -> 60 minutes.
        var (user, login) = await SignInAsync(["Engineer", "OfficeStaff"]);
        Clock.Advance(TimeSpan.FromMinutes(61));

        var idle = await Calls.RefreshMobileAsync(_client, login.String("refreshToken")!);

        Assert.Equal(HttpStatusCode.Unauthorized, idle.Status);
        Assert.Equal("auth.session_expired", idle.Code);
    }

    [Fact]
    public async Task Idle_IsCheckedAtRefreshOnly_NormalRequestsDoNotTouchTheSession()
    {
        var (user, login) = await SignInAsync();
        var before = Assert.Single(await Seed.SessionsOfAsync(_database, user.Id));
        Clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(_client, login.String("accessToken")!)).Status);

        var after = Assert.Single(await Seed.SessionsOfAsync(_database, user.Id));
        Assert.Equal(before.LastRefreshedAt, after.LastRefreshedAt);
    }

    [Fact]
    public async Task AbsoluteLimit_SevenDaysFromLogin_IsNotExtendedByRotation_AndIsNotARevocation()
    {
        var (user, login) = await SignInAsync();
        var token = login.String("refreshToken")!;
        // Stay inside the 8-hour idle timeout until 161 hours after login.
        for (var i = 0; i < 23; i++)
        {
            Clock.Advance(TimeSpan.FromHours(7));
            var refreshed = await Calls.RefreshMobileAsync(_client, token);
            Assert.Equal(HttpStatusCode.OK, refreshed.Status);
            token = refreshed.String("refreshToken")!;
        }

        Clock.Advance(TimeSpan.FromHours(7)); // exactly 168 hours (7 days) after login
        var expired = await Calls.RefreshMobileAsync(_client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, expired.Status);
        Assert.Equal("auth.session_expired", expired.Code);
        var session = Assert.Single(await Seed.SessionsOfAsync(_database, user.Id));
        Assert.Null(session.RevokedAt);
    }

    [Fact]
    public async Task LostAppPermission_RevokesTheSession_With401RefreshInvalid()
    {
        var (user, login) = await SignInAsync(["OfficeStaff"], "admin");
        await Seed.EndRoleAsync(_database, user.Id, "OfficeStaff", Clock.UtcNow);

        var refreshed = await Calls.RefreshMobileAsync(_client, login.String("refreshToken")!);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.Status);
        Assert.Equal("auth.refresh_invalid", refreshed.Code);
        var session = Assert.Single(await Seed.SessionsOfAsync(_database, user.Id));
        Assert.Equal(SessionRevokeReason.AppAccessLost, session.RevokeReason);
    }

    [Fact]
    public async Task DisabledUser_CannotRefresh_AndTheirAccessTokenIsStale()
    {
        var (user, login) = await SignInAsync();
        var admin = await Seed.UserAsync(_database, Clock.UtcNow, ["Admin"]);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<DisableUserUseCase>()
                .ExecuteAsync(new DisableUserRequest(user.Id, admin.Id, "corr-disable", AuditSource.Web));
            Assert.True(result.IsSuccess);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.MeAsync(_client, login.String("accessToken")!)).Status);
        var refreshed = await Calls.RefreshMobileAsync(_client, login.String("refreshToken")!);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.Status);
        Assert.Equal("auth.refresh_invalid", refreshed.Code);
    }

    [Fact]
    public async Task TheHeaderIsIgnoredAtRefresh_TheStoredPlatformShapesTheResponse()
    {
        var (_, login) = await SignInAsync();
        var refreshed = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/refresh",
            new { refreshToken = login.String("refreshToken") },
            headers: new Dictionary<string, string> { ["X-Client-Platform"] = "web" });

        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        Assert.NotNull(refreshed.String("refreshToken"));
        Assert.Empty(refreshed.SetCookies);
    }

    [Fact]
    public async Task Refresh_ReportsTheCurrentMustChangePassword()
    {
        var (_, login) = await SignInAsync(mustChange: true);
        var refreshed = await Calls.RefreshMobileAsync(_client, login.String("refreshToken")!);
        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        Assert.True(refreshed.Json.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Web_FailedRefresh_ClearsTheCookie()
    {
        var response = await Calls.RefreshWebAsync(_client, "unknown-token-value");
        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
        var cleared = Assert.Single(response.SetCookies);
        Assert.StartsWith("contractra_refresh=;", cleared);
        Assert.Contains("max-age=0", cleared, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrentRefreshesOfOneToken_OneRotates_TheOthersGetTheGrace()
    {
        var (user, login) = await SignInAsync();
        var token = login.String("refreshToken")!;

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Calls.RefreshMobileAsync(_client, token)));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Single(results, r => r.String("refreshToken") is not null);
        var session = Assert.Single(await Seed.SessionsOfAsync(_database, user.Id));
        Assert.Null(session.RevokedAt);
        Assert.Equal(2, session.RefreshTokens.Count);
    }

    [Fact]
    public void Startup_RejectsAnIdleTimeoutOutsideTheBounds()
    {
        using var tooShort = new ApiFactory(_database, new Dictionary<string, string?> { ["Auth:IdleTimeout:Engineer"] = "00:04:59" });
        Assert.ThrowsAny<Exception>(() => tooShort.CreateApiClient());
        using var tooLong = new ApiFactory(_database, new Dictionary<string, string?> { ["Auth:IdleTimeout:Admin"] = "1.00:00:01" });
        Assert.ThrowsAny<Exception>(() => tooLong.CreateApiClient());
    }

    [Fact]
    public async Task ConfiguredIdleTimeout_IsUsed()
    {
        await using var factory = new ApiFactory(_database, new Dictionary<string, string?> { ["Auth:IdleTimeout:Engineer"] = "00:10:00" });
        var client = factory.CreateApiClient();
        var user = await Seed.UserAsync(_database, factory.Clock.UtcNow, ["Engineer"]);
        var login = await Calls.LoginAsync(client, user.Login, user.Password);
        factory.Clock.Advance(TimeSpan.FromMinutes(11));

        var refreshed = await Calls.RefreshMobileAsync(client, login.String("refreshToken")!);

        Assert.Equal("auth.session_expired", refreshed.Code);
    }
}
