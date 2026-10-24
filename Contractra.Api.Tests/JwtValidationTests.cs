using System.Net;
using System.Security.Cryptography;
using Contractra.Api.Tests.Support;
using Contractra.Infrastructure.IntegrationTests;
using Microsoft.IdentityModel.Tokens;

namespace Contractra.Api.Tests;

/// <summary>Access-token validation - contract v1.5 §3.2 (E9), §3.2.1 (key ring, rotation), §3.5 (security version), §6.</summary>
public sealed class JwtValidationTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private readonly PostgresTestDatabaseFixture _database;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public JwtValidationTests(PostgresTestDatabaseFixture database)
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

    private async Task<(SeededUser User, Guid SessionId, int SecurityVersion, string AccessToken, string RefreshToken)> SignInAsync(ApiFactory? factory = null)
    {
        factory ??= _factory;
        var client = factory == _factory ? _client : factory.CreateApiClient();
        var user = await Seed.UserAsync(_database, factory.Clock.UtcNow, ["Engineer"]);
        var login = await Calls.LoginAsync(client, user.Login, user.Password);
        Assert.Equal(HttpStatusCode.OK, login.Status);
        var payload = System.Text.Json.JsonDocument.Parse(TokenForge.Decode(login.String("accessToken")!)).RootElement;
        return (user, Guid.Parse(payload.GetProperty("sid").GetString()!), payload.GetProperty("sv").GetInt32(), login.String("accessToken")!, login.String("refreshToken")!);
    }

    private async Task AssertUnauthenticatedAsync(string token)
    {
        var response = await Calls.MeAsync(_client, token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
        Assert.Equal("auth.unauthenticated", response.Code);
    }

    [Fact]
    public async Task ServerIssuedToken_IsAccepted_AndAForgedTokenWithTheSameKeyAndClaimsToo()
    {
        var (user, sid, sv, token, _) = await SignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(_client, token)).Status);

        var forged = TokenForge.Sign(_factory.ActiveKey, _factory.ActiveKeyId, TokenForge.Claims(user.Id, sid, sv), _factory.Clock.UtcNow);
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(_client, forged)).Status);
    }

    [Fact]
    public async Task MissingOrGarbageToken_Is401Unauthenticated()
    {
        var missing = await Calls.SendAsync(_client, HttpMethod.Get, "/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.Status);
        Assert.Equal("auth.unauthenticated", missing.Code);
        await AssertUnauthenticatedAsync("not-a-jwt");
    }

    [Fact]
    public async Task WrongAlgorithm_HS512WithTheRightKey_IsRejected()
    {
        var (user, sid, sv, _, _) = await SignInAsync();
        var key = RandomNumberGenerator.GetBytes(64);
        // Same key material length requirement: sign HS512 with a 512-bit key that is also registered nowhere, and with the real key.
        await AssertUnauthenticatedAsync(TokenForge.Sign(key, _factory.ActiveKeyId, TokenForge.Claims(user.Id, sid, sv), _factory.Clock.UtcNow, algorithm: SecurityAlgorithms.HmacSha512));
        // A ring whose key is long enough for HS512, signed with that exact key and kid:
        // only the HS256 pin can reject it.
        var wideKey = RandomNumberGenerator.GetBytes(64);
        await using var wide = new ApiFactory(_database, new Dictionary<string, string?>
        {
            ["Auth:Jwt:ActiveKeyId"] = "wide",
            ["Auth:Jwt:SigningKeys:test-key-1"] = null,
            ["Auth:Jwt:SigningKeys:wide"] = Convert.ToBase64String(wideKey),
        });
        var wideClient = wide.CreateApiClient();
        var (wideUser, wideSid, wideSv, _, _) = await SignInAsync(wide);
        var claims = TokenForge.Claims(wideUser.Id, wideSid, wideSv);
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(wideClient, TokenForge.Sign(wideKey, "wide", claims, wide.Clock.UtcNow))).Status);

        var hs512 = await Calls.MeAsync(wideClient, TokenForge.Sign(wideKey, "wide", claims, wide.Clock.UtcNow, algorithm: SecurityAlgorithms.HmacSha512));
        Assert.Equal(HttpStatusCode.Unauthorized, hs512.Status);
        // IdentityModel reports a pinned-out algorithm as InvalidAlgorithm or InvalidSignature
        // (it never tries the key with HS512); the HS256 control above used the same key and kid.
        Assert.Matches("SecurityTokenInvalid(Algorithm|Signature)Exception", wide.Logs.AllText);
    }

    [Fact]
    public async Task AlgNone_IsRejected()
    {
        var (user, sid, sv, _, _) = await SignInAsync();
        await AssertUnauthenticatedAsync(TokenForge.Unsigned(TokenForge.Claims(user.Id, sid, sv), _factory.Clock.UtcNow, _factory.ActiveKeyId));
    }

    [Fact]
    public async Task UnknownKid_MissingKid_AndWrongKeyUnderTheRightKid_AreRejected()
    {
        var (user, sid, sv, _, _) = await SignInAsync();
        var claims = TokenForge.Claims(user.Id, sid, sv);
        var now = _factory.Clock.UtcNow;
        await AssertUnauthenticatedAsync(TokenForge.Sign(_factory.ActiveKey, "unknown-kid", claims, now));
        await AssertUnauthenticatedAsync(TokenForge.Sign(_factory.ActiveKey, kid: null, claims, now));
        await AssertUnauthenticatedAsync(TokenForge.Sign(RandomNumberGenerator.GetBytes(32), _factory.ActiveKeyId, claims, now));
    }

    [Fact]
    public async Task WrongIssuerOrAudience_IsRejected()
    {
        var (user, sid, sv, _, _) = await SignInAsync();
        var claims = TokenForge.Claims(user.Id, sid, sv);
        var now = _factory.Clock.UtcNow;
        await AssertUnauthenticatedAsync(TokenForge.Sign(_factory.ActiveKey, _factory.ActiveKeyId, claims, now, issuer: "someone-else"));
        await AssertUnauthenticatedAsync(TokenForge.Sign(_factory.ActiveKey, _factory.ActiveKeyId, claims, now, audience: "other"));
    }

    [Fact]
    public async Task Expiry_IsCheckedWithAtMost30SecondsOfSkew()
    {
        var (user, sid, sv, _, _) = await SignInAsync();
        var claims = TokenForge.Claims(user.Id, sid, sv);
        var now = _factory.Clock.UtcNow;

        var expired20sAgo = TokenForge.Sign(_factory.ActiveKey, _factory.ActiveKeyId, claims, now.AddMinutes(-30).AddSeconds(-20));
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(_client, expired20sAgo)).Status);

        var expired31sAgo = TokenForge.Sign(_factory.ActiveKey, _factory.ActiveKeyId, claims, now.AddMinutes(-30).AddSeconds(-31));
        await AssertUnauthenticatedAsync(expired31sAgo);

        // Not the library default of 5 minutes.
        var expired4MinutesAgo = TokenForge.Sign(_factory.ActiveKey, _factory.ActiveKeyId, claims, now.AddMinutes(-34));
        await AssertUnauthenticatedAsync(expired4MinutesAgo);
    }

    [Fact]
    public async Task NotBefore_IsCheckedWithAtMost30SecondsOfSkew_AndIsRequired()
    {
        var (user, sid, sv, _, _) = await SignInAsync();
        var claims = TokenForge.Claims(user.Id, sid, sv);
        var now = _factory.Clock.UtcNow;

        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(_client,
            TokenForge.Sign(_factory.ActiveKey, _factory.ActiveKeyId, claims, now, notBefore: now.AddSeconds(20)))).Status);
        await AssertUnauthenticatedAsync(TokenForge.Sign(_factory.ActiveKey, _factory.ActiveKeyId, claims, now, notBefore: now.AddSeconds(40)));
        await AssertUnauthenticatedAsync(TokenForge.Sign(_factory.ActiveKey, _factory.ActiveKeyId, claims, now, includeNotBefore: false));
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("sid")]
    [InlineData("app")]
    [InlineData("sv")]
    public async Task MissingRequiredClaim_IsRejected(string claim)
    {
        var (user, sid, sv, _, _) = await SignInAsync();
        var claims = TokenForge.Claims(user.Id, sid, sv);
        claims.Remove(claim);
        await AssertUnauthenticatedAsync(TokenForge.Sign(_factory.ActiveKey, _factory.ActiveKeyId, claims, _factory.Clock.UtcNow));
    }

    [Theory]
    [InlineData("app", "web")]
    [InlineData("app", "Admin")]
    [InlineData("sub", "not-a-guid")]
    [InlineData("sid", "not-a-guid")]
    [InlineData("sv", "two")]
    public async Task MalformedClaim_IsRejected(string claim, string value)
    {
        var (user, sid, sv, _, _) = await SignInAsync();
        var claims = TokenForge.Claims(user.Id, sid, sv);
        claims[claim] = value;
        await AssertUnauthenticatedAsync(TokenForge.Sign(_factory.ActiveKey, _factory.ActiveKeyId, claims, _factory.Clock.UtcNow));
    }

    [Fact]
    public async Task StaleSecurityVersion_IsRejected_AndAFreshRefreshRestoresAccess()
    {
        var (user, _, _, token, refresh) = await SignInAsync();
        await Seed.AssignRoleAsync(_database, user.Id, "OfficeStaff", _factory.Clock.UtcNow);

        await AssertUnauthenticatedAsync(token);

        var refreshed = await Calls.RefreshMobileAsync(_client, refresh);
        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        var me = await Calls.MeAsync(_client, refreshed.String("accessToken")!);
        Assert.Equal(HttpStatusCode.OK, me.Status);
        var payload = System.Text.Json.JsonDocument.Parse(TokenForge.Decode(refreshed.String("accessToken")!)).RootElement;
        Assert.Contains("OfficeStaff", payload.GetProperty("role").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("Apps.Admin.Access", payload.GetProperty("perm").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task TokenForAUserThatDoesNotExist_IsRejected()
    {
        var token = TokenForge.Sign(_factory.ActiveKey, _factory.ActiveKeyId, TokenForge.Claims(Guid.CreateVersion7(), Guid.CreateVersion7(), 1), _factory.Clock.UtcNow);
        await AssertUnauthenticatedAsync(token);
    }

    [Fact]
    public async Task PlannedRotation_KeepsOldTokensValid_AndSignsNewOnesWithTheNewKey()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var newKey = RandomNumberGenerator.GetBytes(32);
        await using var before = new ApiFactory(_database, new Dictionary<string, string?>
        {
            ["Auth:Jwt:ActiveKeyId"] = "old",
            ["Auth:Jwt:SigningKeys:test-key-1"] = null,
            ["Auth:Jwt:SigningKeys:old"] = Convert.ToBase64String(oldKey),
        });
        var (_, _, _, oldToken, _) = await SignInAsync(before);

        await using var after = new ApiFactory(_database, new Dictionary<string, string?>
        {
            ["Auth:Jwt:ActiveKeyId"] = "new",
            ["Auth:Jwt:SigningKeys:test-key-1"] = null,
            ["Auth:Jwt:SigningKeys:old"] = Convert.ToBase64String(oldKey),
            ["Auth:Jwt:SigningKeys:new"] = Convert.ToBase64String(newKey),
        });
        var client = after.CreateApiClient();
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(client, oldToken)).Status);

        var (_, _, _, newToken, _) = await SignInAsync(after);
        var header = System.Text.Json.JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(newToken.Split('.')[0]))).RootElement;
        Assert.Equal("new", header.GetProperty("kid").GetString());
    }

    [Fact]
    public async Task EmergencyRotation_InvalidatesOutstandingAccessTokens_ButSessionsSurviveThroughRefresh()
    {
        var (_, _, _, oldToken, refresh) = await SignInAsync();

        await using var rotated = new ApiFactory(_database, new Dictionary<string, string?>
        {
            ["Auth:Jwt:ActiveKeyId"] = "emergency",
            ["Auth:Jwt:SigningKeys:test-key-1"] = null,
            ["Auth:Jwt:SigningKeys:emergency"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        });
        var client = rotated.CreateApiClient();

        var me = await Calls.MeAsync(client, oldToken);
        Assert.Equal(HttpStatusCode.Unauthorized, me.Status);
        var refreshed = await Calls.RefreshMobileAsync(client, refresh);
        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(client, refreshed.String("accessToken")!)).Status);
    }

    [Theory]
    [InlineData(null, null)]                 // no ring at all
    [InlineData("k1", null)]                 // active key id without its key
    [InlineData("k1", "short")]              // shorter than 256 bits
    [InlineData("k1", "not base64 !!")]      // not base64
    public void Startup_FailsClosed_WithoutAValidSigningKey(string? activeKeyId, string? keyKind)
    {
        var key = keyKind switch
        {
            null => null,
            "short" => Convert.ToBase64String(RandomNumberGenerator.GetBytes(31)),
            _ => keyKind,
        };
        var settings = new Dictionary<string, string?>
        {
            ["Auth:Jwt:ActiveKeyId"] = activeKeyId,
            ["Auth:Jwt:SigningKeys:test-key-1"] = null,
        };
        if (key is not null)
        {
            settings["Auth:Jwt:SigningKeys:k1"] = key;
        }

        using var factory = new ApiFactory(_database, settings);
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());
        Assert.Matches("(?i)(signing key|Auth:Jwt:ActiveKeyId)", exception.ToString());
        if (key is not null)
        {
            Assert.DoesNotContain(key, exception.ToString(), StringComparison.Ordinal);
        }
    }
}
