using System.Net;
using Contractra.Api.Tests.Support;
using Contractra.Application.Audit;
using Contractra.Domain.Audit;
using Contractra.Infrastructure.IntegrationTests;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contractra.Api.Tests;

/// <summary>
/// App audience on every request (contract v1.5 §4.1, E6), the password-change gate (§7),
/// 401 vs 403 (§6), AccessDenied in its own scope with the §14.1 actor_role_code rule
/// (§14, §14.1, §14.2), and that X-Client-Platform is never an authorization input (§5.1).
/// </summary>
public sealed class AudienceAndGateTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private readonly PostgresTestDatabaseFixture _database;
    private GateHost _host = null!;

    public AudienceAndGateTests(PostgresTestDatabaseFixture database)
    {
        _database = database;
    }

    public async Task InitializeAsync() => _host = await GateHost.StartAsync(_database);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private HttpClient Client => _host.Client;

    private async Task<(SeededUser User, string AccessToken)> SignInAsync(string[] roles, string app, bool mustChange = false, string[]? direct = null)
    {
        var user = await Seed.UserAsync(_database, _host.Clock.UtcNow, roles, mustChangePassword: mustChange, directPermissions: direct);
        var login = await Calls.LoginAsync(Client, user.Login, user.Password, app);
        Assert.Equal(HttpStatusCode.OK, login.Status);
        return (user, login.String("accessToken")!);
    }

    private Task<ApiResponse> GetAsync(string path, string? token, IReadOnlyDictionary<string, string>? headers = null) =>
        Calls.SendAsync(Client, HttpMethod.Get, path, bearer: token, headers: headers);

    private async Task<AuditEvent> SingleAccessDeniedAsync(Guid userId)
    {
        var events = await Seed.AuditEventsAsync(_database, userId);
        return Assert.Single(events, e => e.ActionType == AuditActionType.AccessDenied);
    }

    [Fact]
    public async Task MatchingAudience_IsAllowed()
    {
        var (_, admin) = await SignInAsync(["Admin"], "admin");
        var (_, engineer) = await SignInAsync(["Engineer"], "employee");
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/test/admin", admin)).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/test/employee", engineer)).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/test/any", admin)).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/test/any", engineer)).Status);
    }

    [Fact]
    public async Task EmployeeToken_OnAnAdminEndpoint_Is403AppAccessDenied_Audited()
    {
        var (user, token) = await SignInAsync(["Engineer"], "employee");

        var response = await GetAsync("/test/admin/users", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("auth.app_access_denied", response.Code);
        var audit = await SingleAccessDeniedAsync(user.Id);
        Assert.Equal(AuditSource.EmployeeApp, audit.Source);
        Assert.Equal("Engineer", audit.ActorRoleCode);
        Assert.Equal("User", audit.EntityType);
        Assert.Equal(user.Id, audit.EntityId);
        Assert.Equal(response.String("correlationId"), audit.CorrelationId);
        Assert.Equal("auth.app_access_denied: GET /test/admin/users", audit.Reason);
        Assert.True(_host.Logs.Contains("Auth.Forbidden"));
    }

    [Fact]
    public async Task AdminToken_OnAnEmployeeEndpoint_Is403AppAccessDenied()
    {
        var (user, token) = await SignInAsync(["Admin"], "admin");
        var response = await GetAsync("/test/employee", token);
        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("auth.app_access_denied", response.Code);
        Assert.Equal(AuditSource.Web, (await SingleAccessDeniedAsync(user.Id)).Source);
    }

    [Fact]
    public async Task UserHoldingBothApps_TheOtherAppsTokenIsRefused_RoleIsTheOneGrantingTheRequiredPermission()
    {
        // Engineer -> Apps.Employee.Access, OfficeStaff -> Apps.Admin.Access.
        var (user, employeeToken) = await SignInAsync(["Engineer", "OfficeStaff"], "employee");

        var response = await GetAsync("/test/admin", employeeToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("auth.app_access_denied", response.Code);
        // §14.1: the active role through which the actor held the required permission (Apps.Admin.Access).
        Assert.Equal("OfficeStaff", (await SingleAccessDeniedAsync(user.Id)).ActorRoleCode);
    }

    [Fact]
    public async Task AdminAppToken_WithoutTheAppPermissionInItsClaims_IsRefused()
    {
        // A user who lost Apps.Admin.Access: the token for the admin app carries no such perm.
        var (user, _) = await SignInAsync(["Engineer"], "employee");
        var sessions = await Seed.SessionsOfAsync(_database, user.Id);
        var stored = await Seed.UserByIdAsync(_database, user.Id);
        var claims = TokenForge.Claims(user.Id, sessions[0].Id, stored.SecurityVersion, app: "admin");

        var response = await GetAsync("/test/admin", TokenForge.Sign(_host.Key, _host.KeyId, claims, _host.Clock.UtcNow));

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("auth.app_access_denied", response.Code);
    }

    [Fact]
    public async Task UserWithNoActiveRole_IsAuditedWithANullRole()
    {
        var user = await Seed.UserAsync(_database, _host.Clock.UtcNow, []);
        var stored = await Seed.UserByIdAsync(_database, user.Id);
        var token = TokenForge.Sign(_host.Key, _host.KeyId, TokenForge.Claims(user.Id, Guid.CreateVersion7(), stored.SecurityVersion, app: "employee"), _host.Clock.UtcNow);

        var response = await GetAsync("/test/admin", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Null((await SingleAccessDeniedAsync(user.Id)).ActorRoleCode);
    }

    [Fact]
    public async Task NoToken_OnAProtectedEndpoint_Is401_NotAudited()
    {
        foreach (var path in new[] { "/test/admin", "/test/employee", "/test/any", "/test/undeclared" })
        {
            var response = await GetAsync(path, token: null);
            Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
            Assert.Equal("auth.unauthenticated", response.Code);
        }
    }

    [Fact]
    public async Task EndpointWithoutAnAudience_FailsClosed()
    {
        var (user, token) = await SignInAsync(["Admin"], "admin");
        var response = await GetAsync("/test/undeclared", token);
        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("auth.forbidden", response.Code);
        Assert.Equal("auth.forbidden: GET /test/undeclared", (await SingleAccessDeniedAsync(user.Id)).Reason);
    }

    [Fact]
    public async Task PasswordChangeRequired_BlocksEverythingButTheFourAllowedEndpoints()
    {
        var (user, token) = await SignInAsync(["Engineer"], "employee", mustChange: true);

        var blocked = await GetAsync("/test/any", token);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.Status);
        Assert.Equal("auth.password_change_required", blocked.Code);
        var employee = await GetAsync("/test/employee", token);
        Assert.Equal("auth.password_change_required", employee.Code);

        // The §7 list: me, logout, change-password, refresh.
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(Client, token)).Status);

        var audits = (await Seed.AuditEventsAsync(_database, user.Id)).Where(e => e.ActionType == AuditActionType.AccessDenied).ToList();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal("Engineer", a.ActorRoleCode));
    }

    [Fact]
    public async Task PasswordChangeGate_RunsBeforeTheAudienceCheck()
    {
        // An Employee-app token on an admin endpoint, while the change is required: the gate answers first.
        var (_, token) = await SignInAsync(["Engineer"], "employee", mustChange: true);
        var response = await GetAsync("/test/admin", token);
        Assert.Equal("auth.password_change_required", response.Code);
    }

    [Fact]
    public async Task ActorRoleCode_IsTheRoleGrantingTheEndpointPermission_ElseTheFirstActiveRole()
    {
        // Accountant + Admin; Users.Manage comes only from Admin (not the ordinal-first Accountant).
        var (admin, adminToken) = await SignInAsync(["Accountant", "Admin"], "admin", mustChange: true);
        Assert.Equal("auth.password_change_required", (await GetAsync("/test/admin/users", adminToken)).Code);
        Assert.Equal("Admin", (await SingleAccessDeniedAsync(admin.Id)).ActorRoleCode);

        // Payroll.View only as a direct grant -> the first active role by ordinal order.
        var (payroll, payrollToken) = await SignInAsync(["OfficeStaff", "Accountant"], "admin", mustChange: true, direct: ["Payroll.View"]);
        Assert.Equal("auth.password_change_required", (await GetAsync("/test/admin/payroll", payrollToken)).Code);
        Assert.Equal("Accountant", (await SingleAccessDeniedAsync(payroll.Id)).ActorRoleCode);
    }

    [Fact]
    public async Task ClientPlatformHeader_IsNeverAnAuthorizationInput()
    {
        var (_, engineer) = await SignInAsync(["Engineer"], "employee");
        var web = new Dictionary<string, string> { ["X-Client-Platform"] = "web" };
        Assert.Equal("auth.app_access_denied", (await GetAsync("/test/admin", engineer, web)).Code);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/test/employee", engineer, web)).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/test/employee", engineer, new Dictionary<string, string> { ["X-Client-Platform"] = "bogus" })).Status);
    }
}

/// <summary>Contract v1.5 §14.2: a failed AccessDenied write never turns the denial into an allow.</summary>
public sealed class AccessDeniedWriteFailureTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private readonly PostgresTestDatabaseFixture _database;
    private GateHost _host = null!;

    public AccessDeniedWriteFailureTests(PostgresTestDatabaseFixture database)
    {
        _database = database;
    }

    public async Task InitializeAsync() => _host = await GateHost.StartAsync(_database, services =>
    {
        services.RemoveAll<IAuditWriter>();
        services.AddScoped<IAuditWriter, ThrowingAuditWriter>();
    });

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task FailedAuditWrite_StillRefuses_AndLogsAuditWriteFailed()
    {
        var user = await Seed.UserAsync(_database, _host.Clock.UtcNow, ["Engineer"]);
        var login = await Calls.LoginAsync(_host.Client, user.Login, user.Password);

        var response = await Calls.SendAsync(_host.Client, HttpMethod.Get, "/test/admin", bearer: login.String("accessToken"));

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("auth.app_access_denied", response.Code);
        Assert.Empty(await Seed.AuditEventsAsync(_database, user.Id));
        var failure = Assert.Single(_host.Logs.Entries, e => e.Text.Contains("Audit.WriteFailed", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, failure.Level);
        Assert.Contains(response.String("correlationId")!, failure.Text, StringComparison.Ordinal);
        Assert.Contains("GET /test/admin", failure.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedAuditWrite_AtLogin_StillRefuses()
    {
        var user = await Seed.UserAsync(_database, _host.Clock.UtcNow, ["Engineer"]);
        var response = await Calls.LoginAsync(_host.Client, user.Login, user.Password, "admin");
        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("auth.app_access_denied", response.Code);
        Assert.Empty(await Seed.SessionsOfAsync(_database, user.Id));
        Assert.True(_host.Logs.Contains("Audit.WriteFailed"));
    }

    private sealed class ThrowingAuditWriter : IAuditWriter
    {
        public void Stage(AuditEvent auditEvent) => throw new InvalidOperationException("Simulated audit store failure.");
    }
}
