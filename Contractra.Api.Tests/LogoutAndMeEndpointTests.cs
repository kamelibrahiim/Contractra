using System.Net;
using Contractra.Api.Tests.Support;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.IntegrationTests;

namespace Contractra.Api.Tests;

/// <summary>POST /api/v1/auth/logout and GET /api/v1/auth/me - contract v1.5 §5.3, §5.4, §7, §8.</summary>
public sealed class LogoutAndMeEndpointTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private readonly PostgresTestDatabaseFixture _database;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public LogoutAndMeEndpointTests(PostgresTestDatabaseFixture database)
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
    public async Task Me_ReturnsTheDatabaseValues_WithEffectivePermissionsSortedByCode()
    {
        var login = $"Eng.Ahmed.{Guid.NewGuid():N}";
        var user = await Seed.UserAsync(_database, Now, ["Engineer", "OfficeStaff"], login: login);
        var session = await Calls.LoginAsync(_client, login.ToUpperInvariant(), user.Password);

        var me = await Calls.MeAsync(_client, session.String("accessToken")!);

        Assert.Equal(HttpStatusCode.OK, me.Status);
        Assert.Equal(user.Id.ToString(), me.String("id"));
        Assert.Equal(login, me.String("loginIdentifier")); // stored casing, not the typed one
        Assert.Equal(["Engineer", "OfficeStaff"], me.Json.GetProperty("roles").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["Apps.Admin.Access", "Apps.Employee.Access"], me.Json.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()));
        Assert.False(me.Json.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Me_PayrollViewAppearsOnlyAsADirectGrant()
    {
        var admin = await Seed.BootstrapAdminAsync(_database, Now);
        var plainAdmin = await Seed.UserAsync(_database, Now, ["Admin"]);

        var bootstrapMe = await Calls.MeAsync(_client, (await Calls.LoginAsync(_client, admin.Login, admin.Password, "admin")).String("accessToken")!);
        var plainMe = await Calls.MeAsync(_client, (await Calls.LoginAsync(_client, plainAdmin.Login, plainAdmin.Password, "admin")).String("accessToken")!);

        Assert.Equal(
            ["Apps.Admin.Access", "Payroll.View", "Permissions.Grant", "Roles.Manage", "Users.Manage"],
            bootstrapMe.Json.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("Payroll.View", plainMe.Json.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Me_IsAllowedWhileAPasswordChangeIsRequired()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"], mustChangePassword: true);
        var session = await Calls.LoginAsync(_client, user.Login, user.Password);

        var me = await Calls.MeAsync(_client, session.String("accessToken")!);

        Assert.Equal(HttpStatusCode.OK, me.Status);
        Assert.True(me.Json.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Logout_RevokesOnlyTheCurrentSession_And204EvenWhenAlreadyRevoked()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var first = await Calls.LoginAsync(_client, user.Login, user.Password);
        var second = await Calls.LoginAsync(_client, user.Login, user.Password);

        Assert.Equal(HttpStatusCode.NoContent, (await Calls.LogoutAsync(_client, first.String("accessToken")!)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await Calls.LogoutAsync(_client, first.String("accessToken")!)).Status);

        var sessions = await Seed.SessionsOfAsync(_database, user.Id);
        Assert.Single(sessions, s => s.RevokeReason == SessionRevokeReason.Logout);
        Assert.Single(sessions, s => s.RevokedAt is null);
        Assert.Equal(HttpStatusCode.OK, (await Calls.RefreshMobileAsync(_client, second.String("refreshToken")!)).Status);
    }

    [Fact]
    public async Task Logout_WithoutAToken_Is401()
    {
        var response = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/logout");
        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
        Assert.Equal("auth.unauthenticated", response.Code);
    }

    [Fact]
    public async Task Logout_Web_RequiresTheCsrfHeader_SerilogOnly_AndClearsTheCookie()
    {
        var user = await Seed.UserAsync(_database, Now, ["Admin"]);
        var session = await Calls.LoginAsync(_client, user.Login, user.Password, "admin", "web");

        var rejected = await Calls.LogoutAsync(_client, session.String("accessToken")!, csrfHeader: false);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.Status);
        Assert.Equal("auth.forbidden", rejected.Code);
        Assert.Empty(await Seed.AuditEventsAsync(_database, user.Id));
        Assert.Null(Assert.Single(await Seed.SessionsOfAsync(_database, user.Id)).RevokedAt);

        var accepted = await Calls.LogoutAsync(_client, session.String("accessToken")!, csrfHeader: true);
        Assert.Equal(HttpStatusCode.NoContent, accepted.Status);
        var cleared = Assert.Single(accepted.SetCookies);
        Assert.StartsWith("contractra_refresh=;", cleared);
        Assert.Contains("max-age=0", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SessionRevokeReason.Logout, Assert.Single(await Seed.SessionsOfAsync(_database, user.Id)).RevokeReason);
    }

    [Fact]
    public async Task Logout_IsAllowedWhileAPasswordChangeIsRequired()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"], mustChangePassword: true);
        var session = await Calls.LoginAsync(_client, user.Login, user.Password);
        Assert.Equal(HttpStatusCode.NoContent, (await Calls.LogoutAsync(_client, session.String("accessToken")!)).Status);
    }
}
