using System.Net;
using Contractra.Api.Tests.Support;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.IntegrationTests;

namespace Contractra.Api.Tests;

/// <summary>POST /api/v1/auth/change-password - contract v1.5 §3.3 (revocation, rotation), §5.5, §7, §11 (bootstrap admin), §14.</summary>
public sealed class ChangePasswordEndpointTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private readonly PostgresTestDatabaseFixture _database;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public ChangePasswordEndpointTests(PostgresTestDatabaseFixture database)
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
    public async Task Success_RotatesTheCurrentSession_RevokesTheOthers_AndAudits()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var other = await Calls.LoginAsync(_client, user.Login, user.Password);
        var current = await Calls.LoginAsync(_client, user.Login, user.Password);
        var currentSid = System.Text.Json.JsonDocument.Parse(TokenForge.Decode(current.String("accessToken")!)).RootElement.GetProperty("sid").GetString();

        var changed = await Calls.ChangePasswordAsync(_client, current.String("accessToken")!, user.Password, "Brand-New-Passw0rd");

        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.Equal("no-store", changed.Header("Cache-Control"));
        Assert.False(changed.Json.GetProperty("mustChangePassword").GetBoolean());
        var newAccess = changed.String("accessToken")!;
        var newRefresh = changed.String("refreshToken")!;

        // The old access token is stale (security version bumped); the new one works.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.MeAsync(_client, current.String("accessToken")!)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(_client, newAccess)).Status);

        var sessions = await Seed.SessionsOfAsync(_database, user.Id);
        var otherSession = sessions.Single(s => s.Id.ToString() != currentSid);
        var currentSession = sessions.Single(s => s.Id.ToString() == currentSid);
        Assert.Equal(SessionRevokeReason.PasswordChanged, otherSession.RevokeReason);
        Assert.Null(currentSession.RevokedAt);
        Assert.Equal(2, currentSession.RefreshTokens.Count);
        Assert.Single(currentSession.RefreshTokens, t => t.ConsumedAt is null);

        // Old refresh token: consumed. Within 30 s it is the reuse grace; after it, reuse (theft).
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.RefreshMobileAsync(_client, other.String("refreshToken")!)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Calls.RefreshMobileAsync(_client, newRefresh)).Status);

        var audit = Assert.Single(await Seed.AuditEventsAsync(_database, user.Id), e => e.ActionType == AuditActionType.PasswordChanged);
        Assert.Equal(user.Id, audit.EntityId);
        Assert.Equal(AuditSource.EmployeeApp, audit.Source);
        Assert.Equal(changed.String("correlationId") ?? audit.CorrelationId, audit.CorrelationId);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.LoginAsync(_client, user.Login, user.Password)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, user.Login, "Brand-New-Passw0rd")).Status);
    }

    [Fact]
    public async Task TheRefreshTokenConsumedByTheChange_IsInTheReuseGraceFor30Seconds_ThenIsReuse()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var session = await Calls.LoginAsync(_client, user.Login, user.Password);
        var changed = await Calls.ChangePasswordAsync(_client, session.String("accessToken")!, user.Password, "Brand-New-Passw0rd");
        Assert.Equal(HttpStatusCode.OK, changed.Status);

        _factory.Clock.Advance(TimeSpan.FromSeconds(29));
        var grace = await Calls.RefreshMobileAsync(_client, session.String("refreshToken")!);
        Assert.Equal(HttpStatusCode.OK, grace.Status);
        Assert.False(grace.Json.TryGetProperty("refreshToken", out _));

        _factory.Clock.Advance(TimeSpan.FromSeconds(2));
        var reuse = await Calls.RefreshMobileAsync(_client, session.String("refreshToken")!);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.Status);
        Assert.Equal("auth.refresh_invalid", reuse.Code);
        Assert.Equal(SessionRevokeReason.RefreshReuse, Assert.Single(await Seed.SessionsOfAsync(_database, user.Id)).RevokeReason);
    }

    [Fact]
    public async Task Success_CorrelationIdOfTheRequestIsOnTheAuditEvent()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var session = await Calls.LoginAsync(_client, user.Login, user.Password);
        var changed = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/change-password",
            new { currentPassword = user.Password, newPassword = "Brand-New-Passw0rd" },
            bearer: session.String("accessToken"),
            headers: new Dictionary<string, string> { ["X-Correlation-ID"] = "client-corr.123_ABC" });

        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.Equal("client-corr.123_ABC", changed.Header("X-Correlation-ID"));
        var audit = Assert.Single(await Seed.AuditEventsAsync(_database, user.Id), e => e.ActionType == AuditActionType.PasswordChanged);
        Assert.Equal("client-corr.123_ABC", audit.CorrelationId);
    }

    [Fact]
    public async Task Web_TheNewRefreshTokenGoesInTheCookie()
    {
        var user = await Seed.UserAsync(_database, Now, ["Admin"]);
        var session = await Calls.LoginAsync(_client, user.Login, user.Password, "admin", "web");

        var changed = await Calls.ChangePasswordAsync(_client, session.String("accessToken")!, user.Password, "Brand-New-Passw0rd");

        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.False(changed.Json.TryGetProperty("refreshToken", out _));
        Assert.NotNull(Calls.RefreshCookieValue(changed));
        Assert.Equal(AuditSource.Web, Assert.Single(await Seed.AuditEventsAsync(_database, user.Id)).Source);
    }

    [Fact]
    public async Task ForcedChange_ClearsMustChangePassword()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"], mustChangePassword: true);
        var session = await Calls.LoginAsync(_client, user.Login, user.Password);
        Assert.True(session.Json.GetProperty("mustChangePassword").GetBoolean());

        var changed = await Calls.ChangePasswordAsync(_client, session.String("accessToken")!, user.Password, "Brand-New-Passw0rd");

        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.False(changed.Json.GetProperty("mustChangePassword").GetBoolean());
        var me = await Calls.MeAsync(_client, changed.String("accessToken")!);
        Assert.False(me.Json.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task BootstrapAdmin_MayChangeItsOwnPassword()
    {
        var admin = await Seed.BootstrapAdminAsync(_database, Now);
        var session = await Calls.LoginAsync(_client, admin.Login, admin.Password, "admin");

        var changed = await Calls.ChangePasswordAsync(_client, session.String("accessToken")!, admin.Password, "New-Bootstrap-Passw0rd");

        Assert.Equal(HttpStatusCode.OK, changed.Status);
        var stored = await Seed.UserByIdAsync(_database, admin.Id);
        Assert.True(stored.IsBootstrapAdmin);
        Assert.False(stored.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, admin.Login, "New-Bootstrap-Passw0rd", "admin")).Status);
    }

    [Theory]
    [InlineData("Wrong-Current-1", "Brand-New-Passw0rd", "auth.current_password_incorrect")]
    [InlineData(null, "short7!", "auth.password_too_short")]
    [InlineData(null, "", "auth.password_too_short")]
    [InlineData(null, "TOO-LONG", "auth.password_too_long")]
    [InlineData(null, "SAME", "auth.password_same_as_current")]
    public async Task RuleViolation_Is400WithItsCode_AndChangesNothing(string? current, string newPassword, string code)
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var session = await Calls.LoginAsync(_client, user.Login, user.Password);
        newPassword = newPassword switch
        {
            "TOO-LONG" => new string('x', 129),
            "SAME" => user.Password,
            _ => newPassword,
        };

        var response = await Calls.ChangePasswordAsync(_client, session.String("accessToken")!, current ?? user.Password, newPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(code, response.Code);
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(_client, session.String("accessToken")!)).Status);
        Assert.Empty(await Seed.AuditEventsAsync(_database, user.Id));
    }

    [Theory]
    [InlineData("{\"newPassword\":\"Brand-New-Passw0rd\"}", "currentPassword")]
    [InlineData("{\"currentPassword\":\"Correct-Passw0rd\"}", "newPassword")]
    [InlineData("{\"currentPassword\":5,\"newPassword\":\"Brand-New-Passw0rd\"}", "currentPassword")]
    public async Task MissingField_Is400ValidationFailed(string body, string field)
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var session = await Calls.LoginAsync(_client, user.Login, user.Password);

        var response = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/change-password", rawBody: body, bearer: session.String("accessToken"));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
        Assert.True(response.Json.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task OverLimitCurrentPassword_Is400ValidationFailed()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var session = await Calls.LoginAsync(_client, user.Login, user.Password);
        var response = await Calls.ChangePasswordAsync(_client, session.String("accessToken")!, new string('c', 129), "Brand-New-Passw0rd");
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
    }

    [Fact]
    public async Task WithoutAToken_Is401()
    {
        var response = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/change-password", new { currentPassword = "a", newPassword = "Brand-New-Passw0rd" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
        Assert.Equal("auth.unauthenticated", response.Code);
    }

    [Fact]
    public async Task FromALoggedOutSession_Is401_AndChangesNothing()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var session = await Calls.LoginAsync(_client, user.Login, user.Password);
        Assert.Equal(HttpStatusCode.NoContent, (await Calls.LogoutAsync(_client, session.String("accessToken")!)).Status);

        var response = await Calls.ChangePasswordAsync(_client, session.String("accessToken")!, user.Password, "Brand-New-Passw0rd");

        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
        Assert.Equal("auth.unauthenticated", response.Code);
        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, user.Login, user.Password)).Status);
    }
}
