using System.Net;
using Contractra.Api.Tests.Support;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.IntegrationTests;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Contractra.Api.Tests;

/// <summary>POST /api/v1/auth/login - contract v1.5 §5.1, §1 (input limits), §4 (app access), §6, §12, §14.</summary>
public sealed class LoginEndpointTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private readonly PostgresTestDatabaseFixture _database;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public LoginEndpointTests(PostgresTestDatabaseFixture database)
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

    private DateTimeOffset Now => _factory.Clock.UtcNow;

    [Fact]
    public async Task Login_Mobile_Returns200WithBothTokensAndNoStore()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);

        var response = await Calls.LoginAsync(_client, user.Login, user.Password);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.False(string.IsNullOrEmpty(response.String("accessToken")));
        Assert.False(string.IsNullOrEmpty(response.String("refreshToken")));
        Assert.False(response.Json.GetProperty("mustChangePassword").GetBoolean());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", response.String("accessTokenExpiresAt"));
        Assert.Equal("no-store", response.Header("Cache-Control"));
        Assert.Empty(response.SetCookies);

        var sessions = await Seed.SessionsOfAsync(_database, user.Id);
        var session = Assert.Single(sessions);
        Assert.Equal(ClientPlatform.Mobile, session.ClientPlatform);
        Assert.Equal(ClientApp.Employee, session.ClientApp);
        Assert.Equal(session.CreatedAt.AddDays(7), session.AbsoluteExpiresAt);
    }

    [Fact]
    public async Task Login_ExplicitMobileHeader_IsMobile()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var response = await Calls.LoginAsync(_client, user.Login, user.Password, platform: "mobile");
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.NotNull(response.String("refreshToken"));
    }

    [Fact]
    public async Task Login_Web_SetsTheRefreshCookieAndOmitsTheBodyToken()
    {
        var user = await Seed.UserAsync(_database, Now, ["Admin"]);

        var response = await Calls.LoginAsync(_client, user.Login, user.Password, clientApp: "admin", platform: "web");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.False(response.Json.TryGetProperty("refreshToken", out _));
        var cookie = Assert.Single(response.SetCookies);
        Assert.StartsWith("contractra_refresh=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);

        var session = Assert.Single(await Seed.SessionsOfAsync(_database, user.Id));
        Assert.Equal(ClientPlatform.Web, session.ClientPlatform);
        var expires = DateTimeOffset.Parse(cookie.Split(';').Single(p => p.Trim().StartsWith("expires=", StringComparison.OrdinalIgnoreCase)).Trim()[8..]);
        Assert.Equal(session.AbsoluteExpiresAt.ToUnixTimeSeconds(), expires.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Login_UnknownClientPlatform_Is400ValidationFailed()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var response = await Calls.LoginAsync(_client, user.Login, user.Password, platform: "desktop");
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
        Assert.True(response.Json.GetProperty("errors").TryGetProperty("X-Client-Platform", out _));
    }

    [Theory]
    [InlineData("{\"password\":\"x\",\"clientApp\":\"employee\"}", "loginIdentifier")]
    [InlineData("{\"loginIdentifier\":\"someone\",\"clientApp\":\"employee\"}", "password")]
    [InlineData("{\"loginIdentifier\":\"someone\",\"password\":\"\",\"clientApp\":\"employee\"}", "password")]
    [InlineData("{\"loginIdentifier\":\"someone\",\"password\":\"x\"}", "clientApp")]
    [InlineData("{\"loginIdentifier\":\"someone\",\"password\":\"x\",\"clientApp\":\"office\"}", "clientApp")]
    [InlineData("{\"loginIdentifier\":\"someone\",\"password\":\"x\",\"clientApp\":\"Admin\"}", "clientApp")]
    [InlineData("{\"loginIdentifier\":42,\"password\":\"x\",\"clientApp\":\"admin\"}", "loginIdentifier")]
    public async Task Login_MissingOrInvalidField_Is400ValidationFailedWithFieldErrors(string body, string field)
    {
        var response = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/login", rawBody: body);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
        Assert.Equal("application/problem+json", response.Message.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Json.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public async Task Login_MalformedBody_Is400ValidationFailed(string body)
    {
        var response = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/login", rawBody: body);
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
    }

    [Fact]
    public async Task Login_EveryCredentialFailure_IsTheSameGeneric401()
    {
        var now = Now;
        var active = await Seed.UserAsync(_database, now, ["Engineer"]);
        var disabled = await Seed.UserAsync(_database, now, ["Engineer"], disabled: true);
        var unusable = await Seed.UserAsync(_database, now, ["Engineer"], passwordHash: User.UnusablePasswordHash);
        var expiredTemporary = await Seed.UserAsync(
            _database, now, ["Engineer"], mustChangePassword: true, temporaryPasswordExpiresAt: now.AddMinutes(-1));

        var responses = new[]
        {
            await Calls.LoginAsync(_client, Seed.NewLogin("nobody"), "Whatever-1"),
            await Calls.LoginAsync(_client, active.Login, "Wrong-Passw0rd"),
            await Calls.LoginAsync(_client, disabled.Login, disabled.Password),
            await Calls.LoginAsync(_client, unusable.Login, unusable.Password),
            await Calls.LoginAsync(_client, expiredTemporary.Login, expiredTemporary.Password),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
            Assert.Equal("auth.invalid_credentials", response.Code);
            Assert.Equal("Invalid credentials", response.String("title"));
            Assert.False(response.Json.TryGetProperty("errors", out _));
            Assert.False(response.Json.TryGetProperty("detail", out _));
        }

        // Identical apart from the correlation id.
        var shapes = responses.Select(r => string.Join(",", r.Json.EnumerateObject().Where(p => p.Name != "correlationId").Select(p => $"{p.Name}={p.Value}"))).Distinct();
        Assert.Single(shapes);

        // The real reasons go to Serilog only.
        Assert.True(_factory.Logs.Contains("unknown_identifier"));
        Assert.True(_factory.Logs.Contains("wrong_password"));
        Assert.True(_factory.Logs.Contains("account_disabled"));
        Assert.True(_factory.Logs.Contains("no_usable_password"));
        Assert.True(_factory.Logs.Contains("temporary_password_expired"));
        Assert.Empty(await Seed.SessionsOfAsync(_database, active.Id));
    }

    [Fact]
    public async Task Login_IdentifierIsCaseInsensitiveButNotTrimmed()
    {
        var login = $"Kamel.{Guid.NewGuid():N}";
        var user = await Seed.UserAsync(_database, Now, ["Engineer"], login: login);

        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, login.ToUpperInvariant(), user.Password)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, login.ToLowerInvariant(), user.Password)).Status);
        var padded = await Calls.LoginAsync(_client, $" {login} ", user.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, padded.Status);
        Assert.Equal("auth.invalid_credentials", padded.Code);
    }

    [Fact]
    public async Task Login_OverLimitInput_IsTheGeneric401()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);

        var longIdentifier = await Calls.LoginAsync(_client, new string('a', 257), "x");
        var longPassword = await Calls.LoginAsync(_client, user.Login, new string('p', 129));
        var hugeBody = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/login",
            rawBody: $"{{\"loginIdentifier\":\"{user.Login}\",\"password\":\"{new string('p', 17 * 1024)}\",\"clientApp\":\"employee\"}}");

        foreach (var response in new[] { longIdentifier, longPassword, hugeBody })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
            Assert.Equal("auth.invalid_credentials", response.Code);
        }

        Assert.True(_factory.Logs.Contains("input_over_limit"));
    }

    [Fact]
    public async Task Login_CorrectPasswordWithoutAppPermission_Is403AppAccessDenied_AuditedWithTheActorsRole()
    {
        var engineer = await Seed.UserAsync(_database, Now, ["Engineer"]);

        var response = await Calls.LoginAsync(_client, engineer.Login, engineer.Password, clientApp: "admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("auth.app_access_denied", response.Code);
        Assert.Empty(await Seed.SessionsOfAsync(_database, engineer.Id));

        var audit = Assert.Single(await Seed.AuditEventsAsync(_database, engineer.Id));
        Assert.Equal(AuditActionType.AccessDenied, audit.ActionType);
        Assert.Equal(AuditActorType.User, audit.ActorType);
        Assert.Equal("User", audit.EntityType);
        Assert.Equal(engineer.Id, audit.EntityId);
        Assert.Equal(AuditSource.Web, audit.Source); // the Admin app was asked for
        // §14.1: Apps.Admin.Access came from no role, so the first active role by ordinal order.
        Assert.Equal("Engineer", audit.ActorRoleCode);
        Assert.Equal(response.String("correlationId"), audit.CorrelationId);
        Assert.Equal("auth.app_access_denied: POST /api/v1/auth/login", audit.Reason);
        Assert.True(_factory.Logs.Contains("Auth.Forbidden"));
    }

    [Fact]
    public async Task Login_DisabledAccountWithCorrectPasswordForTheWrongApp_IsStillTheGeneric401_NotAudited()
    {
        var disabled = await Seed.UserAsync(_database, Now, ["Engineer"], disabled: true);

        var response = await Calls.LoginAsync(_client, disabled.Login, disabled.Password, clientApp: "admin");

        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
        Assert.Equal("auth.invalid_credentials", response.Code);
        Assert.Empty(await Seed.AuditEventsAsync(_database, disabled.Id));
    }

    [Fact]
    public async Task Login_UserOnTemporaryPassword_ReportsMustChangePassword()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"], mustChangePassword: true);
        var response = await Calls.LoginAsync(_client, user.Login, user.Password);
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.True(response.Json.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Login_BootstrapAdmin_CanSignInToTheAdminApp_WithoutForcedChange()
    {
        var admin = await Seed.BootstrapAdminAsync(_database, Now);
        var response = await Calls.LoginAsync(_client, admin.Login, admin.Password, clientApp: "admin");
        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.False(response.Json.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Login_HashNeedingAnUpgrade_IsTransparentlyRehashed_WithoutASecurityVersionChange()
    {
        var v2Hasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2 }));
        var password = "Legacy-Passw0rd";
        var legacyHash = v2Hasher.HashPassword(null!, password);
        var user = await Seed.UserAsync(_database, Now, ["Engineer"], password: password, passwordHash: legacyHash);
        var before = await Seed.UserByIdAsync(_database, user.Id);

        var response = await Calls.LoginAsync(_client, user.Login, password);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var after = await Seed.UserByIdAsync(_database, user.Id);
        Assert.NotEqual(legacyHash, after.PasswordHash);
        Assert.Equal(before.SecurityVersion, after.SecurityVersion);
        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, user.Login, password)).Status);
    }

    [Fact]
    public async Task Login_SuccessIsLoggedToSerilog_WithoutAnyAuditEvent()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, user.Login, user.Password)).Status);
        Assert.True(_factory.Logs.Contains("Auth.LoginSucceeded"));
        Assert.Empty(await Seed.AuditEventsAsync(_database, user.Id));
    }

    [Fact]
    public async Task Login_TokenCarriesTheContractClaims()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var response = await Calls.LoginAsync(_client, user.Login, user.Password);

        var token = response.String("accessToken")!;
        var header = System.Text.Json.JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(token.Split('.')[0]))).RootElement;
        Assert.Equal("HS256", header.GetProperty("alg").GetString());
        Assert.Equal(_factory.ActiveKeyId, header.GetProperty("kid").GetString());

        var payload = System.Text.Json.JsonDocument.Parse(TokenForge.Decode(token)).RootElement;
        Assert.Equal(user.Id.ToString("D"), payload.GetProperty("sub").GetString());
        Assert.Equal("employee", payload.GetProperty("app").GetString());
        Assert.Equal("contractra-api", payload.GetProperty("iss").GetString());
        Assert.Equal("contractra", payload.GetProperty("aud").GetString());
        Assert.Equal(payload.GetProperty("iat").GetInt64(), payload.GetProperty("nbf").GetInt64());
        Assert.Equal(payload.GetProperty("iat").GetInt64() + 1800, payload.GetProperty("exp").GetInt64());
        Assert.Equal(2, payload.GetProperty("sv").GetInt32()); // created with one role (§3.5)
        Assert.Equal(["Apps.Employee.Access"], payload.GetProperty("perm").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["Engineer"], payload.GetProperty("role").EnumerateArray().Select(e => e.GetString()));
        var session = Assert.Single(await Seed.SessionsOfAsync(_database, user.Id));
        Assert.Equal(session.Id.ToString("D"), payload.GetProperty("sid").GetString());
        Assert.False(payload.TryGetProperty("mustChangePassword", out _));
    }
}
