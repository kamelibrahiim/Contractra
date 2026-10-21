using System.Net;
using System.Text.Json;
using Contractra.Api.Tests.Support;
using Contractra.Application.Identity;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Domain.Parties;
using Contractra.Infrastructure.IntegrationTests;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Api.Tests;

/// <summary>
/// /api/v1/users and /api/v1/roles - contract v1.5 §9 (Backend Sprint 2, Slice 7): the §9
/// check order, the response contracts, the derived credentialState and the audit rows,
/// against an ephemeral PostgreSQL database.
/// </summary>
public sealed class UsersEndpointTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private readonly PostgresTestDatabaseFixture _database;
    private readonly RecordingDelivery _delivery = new();
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public UsersEndpointTests(PostgresTestDatabaseFixture database)
    {
        _database = database;
    }

    /// <summary>A reset-delivery channel standing in for the one OC-2 has not decided; it only exists in these tests.</summary>
    private sealed class RecordingDelivery : IPasswordResetDelivery
    {
        public string Channel => "test-channel";

        public bool Reachable { get; set; } = true;

        public bool ThrowOnSend { get; set; }

        public List<(Guid UserId, string Code)> Sent { get; } = [];

        public Task<bool> CanReachAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(Reachable);

        public Task SendAsync(Guid userId, string code, CancellationToken cancellationToken = default)
        {
            Sent.Add((userId, code));
            return ThrowOnSend ? Task.FromException(new InvalidOperationException("channel down")) : Task.CompletedTask;
        }
    }

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_database, configureServices: services => services.AddSingleton<IPasswordResetDelivery>(_delivery));
        _client = _factory.CreateApiClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private DateTimeOffset Now => _factory.Clock.UtcNow;

    private async Task<(SeededUser User, string Token)> SignInAsync(string[] roles, string app = "admin", bool mustChange = false)
    {
        var user = await Seed.UserAsync(_database, Now, roles, mustChangePassword: mustChange);
        var login = await Calls.LoginAsync(_client, user.Login, user.Password, app);
        Assert.Equal(HttpStatusCode.OK, login.Status);
        return (user, login.String("accessToken")!);
    }

    private Task<(SeededUser User, string Token)> AdminAsync() => SignInAsync(["Admin"]);

    private Task<ApiResponse> SendAsync(HttpMethod method, string url, string? token, object? body = null, string? rawBody = null) =>
        Calls.SendAsync(_client, method, url, body, token, rawBody: rawBody);

    private Task<ApiResponse> CreateAsync(string token, object body) => SendAsync(HttpMethod.Post, "/api/v1/users", token, body);

    private async Task<Party> SeedPartyAsync(PartyStatus status = PartyStatus.Active, bool engineerRole = true)
    {
        await using var db = _database.CreateDbContext();
        var party = new Party($"Engineer {Guid.NewGuid():N}", status);
        if (engineerRole)
        {
            party.AssignRole(new PartyRoleCode("Engineer"), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30));
        }

        db.Parties.Add(party);
        await db.SaveChangesAsync();
        return party;
    }

    /// <summary>At most one bootstrap admin can exist per database (ux_users_is_bootstrap_admin), and the class shares one database.</summary>
    private async Task<SeededUser> BootstrapAdminAsync()
    {
        await using (var db = _database.CreateDbContext())
        {
            var existing = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.IsBootstrapAdmin);
            if (existing is not null)
            {
                return new SeededUser(existing.Id, existing.LoginIdentifier.Value, string.Empty);
            }
        }

        return await Seed.BootstrapAdminAsync(_database, Now);
    }

    private static string[] Roles(JsonElement user) => user.GetProperty("roles").EnumerateArray().Select(e => e.GetString()!).ToArray();

    private async Task<List<AuditEvent>> AuditOfAsync(Guid actorId, AuditActionType action) =>
        (await Seed.AuditEventsAsync(_database, actorId)).Where(e => e.ActionType == action).ToList();

    // ---- §9 check order, steps 1-4 -------------------------------------------------

    public static TheoryData<string, string> AllEndpoints => new()
    {
        { "POST", "/api/v1/users" },
        { "GET", "/api/v1/users" },
        { "GET", "/api/v1/users/{id}" },
        { "POST", "/api/v1/users/{id}/disable" },
        { "POST", "/api/v1/users/{id}/reactivate" },
        { "POST", "/api/v1/users/{id}/reset-password" },
        { "POST", "/api/v1/users/{id}/initial-password" },
        { "GET", "/api/v1/roles" },
    };

    private static string Url(string template, Guid id) => template.Replace("{id}", id.ToString());

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task WithoutAToken_Is401Unauthenticated(string method, string template)
    {
        var response = await SendAsync(new HttpMethod(method), Url(template, Guid.NewGuid()), token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
        Assert.Equal("auth.unauthenticated", response.Code);
    }

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task WhileAPasswordChangeIsRequired_Is403PasswordChangeRequired(string method, string template)
    {
        var (_, token) = await SignInAsync(["Admin"], mustChange: true);

        var response = await SendAsync(new HttpMethod(method), Url(template, Guid.NewGuid()), token);

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("auth.password_change_required", response.Code);
    }

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task AnEmployeeAppToken_Is403AppAccessDenied(string method, string template)
    {
        var (user, token) = await SignInAsync(["Engineer"], "employee");

        var response = await SendAsync(new HttpMethod(method), Url(template, Guid.NewGuid()), token);

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("auth.app_access_denied", response.Code);
        Assert.Single(await AuditOfAsync(user.Id, AuditActionType.AccessDenied));
    }

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task AnAdminAppUserWithoutUsersManage_Is403Forbidden_AndAudited(string method, string template)
    {
        var (user, token) = await SignInAsync(["OfficeStaff"]);

        var response = await SendAsync(new HttpMethod(method), Url(template, Guid.NewGuid()), token);

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("auth.forbidden", response.Code);
        var denied = Assert.Single(await AuditOfAsync(user.Id, AuditActionType.AccessDenied));
        Assert.Equal(AuditSource.Web, denied.Source);
        Assert.Equal("OfficeStaff", denied.ActorRoleCode);
        Assert.StartsWith("auth.forbidden: ", denied.Reason);
        Assert.Equal(response.String("correlationId"), denied.CorrelationId);
        Assert.True(_factory.Logs.Contains("Auth.Forbidden"));
    }

    [Fact]
    public async Task ACallerWithoutThePermission_NeverLearnsWhetherAnIdIsTheBootstrapAdmin()
    {
        var bootstrap = await BootstrapAdminAsync();
        var (_, token) = await SignInAsync(["OfficeStaff"]);

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/users/{bootstrap.Id}/disable", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("auth.forbidden", response.Code);
    }

    // ---- POST /users - §9.1 --------------------------------------------------------

    [Fact]
    public async Task Create_AnEngineerWithAParty_Is201WithTheContractBody_AndAuditsInTheSameTransaction()
    {
        var (admin, token) = await AdminAsync();
        var party = await SeedPartyAsync();
        var login = Seed.NewLogin("eng");

        var response = await CreateAsync(token, new { loginIdentifier = login, roleCodes = new[] { "engineer" }, partyId = party.Id });

        Assert.Equal(HttpStatusCode.Created, response.Status);
        var user = response.Json.GetProperty("user");
        var id = Guid.Parse(user.GetProperty("id").GetString()!);
        Assert.Equal($"/api/v1/users/{id}", response.Header("Location"));
        Assert.Equal("no-store", response.Header("Cache-Control"));
        Assert.Equal(login, user.GetProperty("loginIdentifier").GetString());
        Assert.Equal("Active", user.GetProperty("status").GetString());
        Assert.Equal(["Engineer"], Roles(user)); // canonical casing, not the requested one
        Assert.False(user.GetProperty("isBootstrapAdmin").GetBoolean());
        Assert.Equal("TemporaryPasswordIssued", user.GetProperty("credentialState").GetString());
        var temporaryPassword = response.String("temporaryPassword")!;
        Assert.Equal(12, temporaryPassword.Length);
        Assert.Equal(Now.AddHours(72).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), response.String("temporaryPasswordExpiresAt"));
        Assert.Equal(["user", "temporaryPassword", "temporaryPasswordExpiresAt"], response.Json.EnumerateObject().Select(p => p.Name));

        var stored = await Seed.UserByIdAsync(_database, id);
        Assert.True(stored.MustChangePassword);
        await using (var db = _database.CreateDbContext())
        {
            Assert.Equal(party.Id, (await db.UserPartyLinks.SingleAsync(l => l.UserId == id)).PartyId);
        }

        var events = await Seed.AuditEventsAsync(_database, admin.Id);
        var created = Assert.Single(events, e => e.ActionType == AuditActionType.UserCreated && e.EntityId == id);
        Assert.Equal(AuditSource.Web, created.Source);
        Assert.Equal("Admin", created.ActorRoleCode);
        Assert.Equal(response.Header("X-Correlation-ID"), created.CorrelationId);
        Assert.Single(events, e => e.ActionType == AuditActionType.RoleAssigned && e.EntityId == id);

        // The temporary password works once (the user must then change it) and is stored nowhere readable.
        var signIn = await Calls.LoginAsync(_client, login, temporaryPassword);
        Assert.Equal(HttpStatusCode.OK, signIn.Status);
        Assert.True(signIn.Json.GetProperty("mustChangePassword").GetBoolean());
        Assert.DoesNotContain(temporaryPassword, await Seed.AuditTableTextAsync(_database));
        Assert.DoesNotContain(temporaryPassword, _factory.Logs.AllText);
    }

    [Fact]
    public async Task Create_ANonEngineerWithoutAParty_Is201()
    {
        var (_, token) = await AdminAsync();

        var response = await CreateAsync(token, new { loginIdentifier = Seed.NewLogin("office"), roleCodes = new[] { "OfficeStaff", "Accountant" } });

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.Equal(["Accountant", "OfficeStaff"], Roles(response.Json.GetProperty("user")));
    }

    public static TheoryData<string> MalformedBodies => new()
    {
        "[1,2]",
        "not json",
        "{\"loginIdentifier\":123,\"roleCodes\":[\"OfficeStaff\"]}",
        "{\"loginIdentifier\":\"x1\",\"roleCodes\":\"OfficeStaff\"}",
        "{\"loginIdentifier\":\"x2\",\"roleCodes\":[\"OfficeStaff\",5]}",
        "{\"loginIdentifier\":\"x3\",\"roleCodes\":[\"Engineer\"],\"partyId\":\"not-a-guid\"}",
        "{\"loginIdentifier\":\"x4\",\"roleCodes\":[\"Engineer\"],\"partyId\":7}",
    };

    [Theory]
    [MemberData(nameof(MalformedBodies))]
    public async Task Create_WithAWrongShape_Is400ValidationFailed_AndCreatesNothing(string raw)
    {
        var (_, token) = await AdminAsync();
        var before = await CountUsersAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/users", token, rawBody: raw);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
        Assert.Equal(before, await CountUsersAsync());
    }

    [Fact]
    public async Task Create_ValidationFailed_NamesTheOffendingField()
    {
        var (_, token) = await AdminAsync();

        var roles = await SendAsync(HttpMethod.Post, "/api/v1/users", token, rawBody: "{\"loginIdentifier\":\"x\",\"roleCodes\":\"OfficeStaff\"}");
        var party = await SendAsync(HttpMethod.Post, "/api/v1/users", token, rawBody: "{\"loginIdentifier\":\"x\",\"roleCodes\":[\"Engineer\"],\"partyId\":\"nope\"}");

        Assert.True(roles.Json.GetProperty("errors").TryGetProperty("roleCodes", out _));
        Assert.True(party.Json.GetProperty("errors").TryGetProperty("partyId", out _));
    }

    [Fact]
    public async Task Create_DuplicateRoleCodes_AndAPartyForANonEngineer_AreValidationFailed()
    {
        var (_, token) = await AdminAsync();
        var party = await SeedPartyAsync();

        var duplicate = await CreateAsync(token, new { loginIdentifier = Seed.NewLogin(), roleCodes = new[] { "OfficeStaff", "officestaff" } });
        var stray = await CreateAsync(token, new { loginIdentifier = Seed.NewLogin(), roleCodes = new[] { "OfficeStaff" }, partyId = party.Id });

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.Status);
        Assert.Equal("validation.failed", duplicate.Code);
        Assert.True(duplicate.Json.GetProperty("errors").TryGetProperty("roleCodes", out _));
        Assert.Equal(HttpStatusCode.BadRequest, stray.Status);
        Assert.Equal("validation.failed", stray.Code);
        Assert.True(stray.Json.GetProperty("errors").TryGetProperty("partyId", out _));
    }

    [Fact]
    public async Task Create_BusinessRuleFailures_UseTheDocumentedCodes()
    {
        var (_, token) = await AdminAsync();
        var inactive = await SeedPartyAsync(PartyStatus.Inactive);
        var needsClassification = await SeedPartyAsync(PartyStatus.NeedsClassification);
        var noRole = await SeedPartyAsync(engineerRole: false);

        async Task<ApiResponse> Post(object body) => await CreateAsync(token, body);

        var invalidLogin = await Post(new { loginIdentifier = " padded ", roleCodes = new[] { "OfficeStaff" } });
        var emptyLogin = await SendAsync(HttpMethod.Post, "/api/v1/users", token, rawBody: "{\"roleCodes\":[\"OfficeStaff\"]}");
        var tooLong = await Post(new { loginIdentifier = new string('a', 257), roleCodes = new[] { "OfficeStaff" } });
        var noRoles = await Post(new { loginIdentifier = Seed.NewLogin(), roleCodes = Array.Empty<string>() });
        var absentRoles = await SendAsync(HttpMethod.Post, "/api/v1/users", token, rawBody: "{\"loginIdentifier\":\"abc\"}");
        var unknownRole = await Post(new { loginIdentifier = Seed.NewLogin(), roleCodes = new[] { "Wizard" } });
        var noParty = await Post(new { loginIdentifier = Seed.NewLogin(), roleCodes = new[] { "Engineer" } });
        var missingParty = await Post(new { loginIdentifier = Seed.NewLogin(), roleCodes = new[] { "Engineer" }, partyId = Guid.NewGuid() });
        var notActive = await Post(new { loginIdentifier = Seed.NewLogin(), roleCodes = new[] { "Engineer" }, partyId = inactive.Id });
        var notClassified = await Post(new { loginIdentifier = Seed.NewLogin(), roleCodes = new[] { "Engineer" }, partyId = needsClassification.Id });
        var noEngineerRole = await Post(new { loginIdentifier = Seed.NewLogin(), roleCodes = new[] { "Engineer" }, partyId = noRole.Id });

        Assert.All([invalidLogin, emptyLogin, tooLong], r => Assert.Equal("users.login_identifier_invalid", r.Code));
        Assert.All([noRoles, absentRoles], r => Assert.Equal("users.role_required", r.Code));
        Assert.Equal("users.role_unknown", unknownRole.Code);
        Assert.Equal("users.party_required", noParty.Code);
        Assert.All(
            [invalidLogin, emptyLogin, tooLong, noRoles, absentRoles, unknownRole, noParty, missingParty, notActive, notClassified, noEngineerRole],
            r => Assert.Equal(HttpStatusCode.BadRequest, r.Status));
        Assert.Equal("users.party_invalid", missingParty.Code);
        Assert.Equal("not_found", missingParty.String("reason"));
        Assert.Equal("not_active", notActive.String("reason"));
        Assert.Equal("not_active", notClassified.String("reason"));
        Assert.Equal("no_active_engineer_role", noEngineerRole.String("reason"));
        Assert.Null(invalidLogin.String("reason"));
    }

    [Fact]
    public async Task Create_ADuplicateLoginIdentifier_IsCaseInsensitive409_AndAPartyCanBeLinkedOnce()
    {
        var (_, token) = await AdminAsync();
        var party = await SeedPartyAsync();
        var login = Seed.NewLogin("Dup");
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(token, new { loginIdentifier = login, roleCodes = new[] { "Engineer" }, partyId = party.Id })).Status);

        var taken = await CreateAsync(token, new { loginIdentifier = login.ToUpperInvariant(), roleCodes = new[] { "OfficeStaff" } });
        var linked = await CreateAsync(token, new { loginIdentifier = Seed.NewLogin(), roleCodes = new[] { "Engineer" }, partyId = party.Id });

        Assert.Equal(HttpStatusCode.Conflict, taken.Status);
        Assert.Equal("users.login_identifier_taken", taken.Code);
        Assert.Equal(HttpStatusCode.Conflict, linked.Status);
        Assert.Equal("users.party_already_linked", linked.Code);
    }

    [Fact]
    public async Task Create_ConcurrentRequestsForTheSameLogin_OneWins_TheOtherIs409Taken()
    {
        var (_, token) = await AdminAsync();
        var login = Seed.NewLogin("race");
        var body = new { loginIdentifier = login, roleCodes = new[] { "OfficeStaff" } };

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CreateAsync(token, body)));

        Assert.Equal(1, responses.Count(r => r.Status == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.Status != HttpStatusCode.Created), r =>
        {
            Assert.Equal(HttpStatusCode.Conflict, r.Status);
            Assert.Equal("users.login_identifier_taken", r.Code);
        });
    }

    [Fact]
    public async Task Create_ConcurrentRequestsForTheSameParty_OneWins_TheOtherIs409AlreadyLinked()
    {
        var (_, token) = await AdminAsync();
        var party = await SeedPartyAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            CreateAsync(token, new { loginIdentifier = Seed.NewLogin("racep"), roleCodes = new[] { "Engineer" }, partyId = party.Id })));

        Assert.Equal(1, responses.Count(r => r.Status == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.Status != HttpStatusCode.Created), r =>
        {
            Assert.Equal(HttpStatusCode.Conflict, r.Status);
            Assert.Equal("users.party_already_linked", r.Code);
        });
    }

    private async Task<int> CountUsersAsync()
    {
        await using var db = _database.CreateDbContext();
        return await db.Users.CountAsync();
    }

    // ---- GET /users, GET /users/{id} - §9.2, §9.3 -----------------------------------

    [Fact]
    public async Task List_ReturnsThePagedShape_SortedCaseInsensitively_WithSearchAndStatusFilters()
    {
        var (_, token) = await AdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..10];
        var bravo = await Seed.UserAsync(_database, Now, ["OfficeStaff"], login: $"bravo.{tag}");
        var alpha = await Seed.UserAsync(_database, Now, ["Engineer"], login: $"Alpha.{tag}");
        var charlie = await Seed.UserAsync(_database, Now, ["Accountant"], login: $"charlie.{tag}", disabled: true);

        var all = await SendAsync(HttpMethod.Get, $"/api/v1/users?search={tag.ToUpperInvariant()}", token);
        var disabled = await SendAsync(HttpMethod.Get, $"/api/v1/users?search={tag}&status=Disabled", token);
        var secondPage = await SendAsync(HttpMethod.Get, $"/api/v1/users?search={tag}&page=2&pageSize=2", token);

        Assert.Equal(HttpStatusCode.OK, all.Status);
        Assert.Equal(1, all.Json.GetProperty("page").GetInt32());
        Assert.Equal(20, all.Json.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, all.Json.GetProperty("totalCount").GetInt32());
        var items = all.Json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([alpha.Login, bravo.Login, charlie.Login], items.Select(i => i.GetProperty("loginIdentifier").GetString()));
        Assert.Equal(["Active", "Active", "Disabled"], items.Select(i => i.GetProperty("status").GetString()));
        Assert.Equal(["Engineer"], Roles(items[0]));
        Assert.All(items, i => Assert.Equal("PasswordSet", i.GetProperty("credentialState").GetString()));
        Assert.All(items, i => Assert.False(i.GetProperty("isBootstrapAdmin").GetBoolean()));
        Assert.Equal(
            ["id", "loginIdentifier", "status", "roles", "isBootstrapAdmin", "credentialState"],
            items[0].EnumerateObject().Select(p => p.Name));

        Assert.Equal(1, disabled.Json.GetProperty("totalCount").GetInt32());
        Assert.Equal(charlie.Login, disabled.Json.GetProperty("items")[0].GetProperty("loginIdentifier").GetString());

        Assert.Equal(2, secondPage.Json.GetProperty("page").GetInt32());
        Assert.Equal(3, secondPage.Json.GetProperty("totalCount").GetInt32());
        Assert.Equal([charlie.Login], secondPage.Json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("loginIdentifier").GetString()));
    }

    [Fact]
    public async Task List_SearchTreatsWildcardsLiterally_AndAPageBeyondTheEndIsEmpty()
    {
        var (_, token) = await AdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..10];
        await Seed.UserAsync(_database, Now, ["OfficeStaff"], login: $"w.{tag}");

        var wildcard = await SendAsync(HttpMethod.Get, $"/api/v1/users?search=%25{tag}", token);
        var beyond = await SendAsync(HttpMethod.Get, $"/api/v1/users?search={tag}&page=9", token);

        Assert.Equal(0, wildcard.Json.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, beyond.Json.GetProperty("totalCount").GetInt32());
        Assert.Empty(beyond.Json.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Roles_OfAUser_AreOnlyTheCurrentlyActiveOnes()
    {
        var (_, token) = await AdminAsync();
        var user = await Seed.UserAsync(_database, Now, ["OfficeStaff", "Accountant"]);
        await Seed.EndRoleAsync(_database, user.Id, "Accountant", Now);

        var read = await SendAsync(HttpMethod.Get, $"/api/v1/users/{user.Id}", token);
        var list = await SendAsync(HttpMethod.Get, $"/api/v1/users?search={user.Login}", token);

        Assert.Equal(["OfficeStaff"], Roles(read.Json));
        Assert.Equal(["OfficeStaff"], Roles(list.Json.GetProperty("items")[0]));
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("page=-1", "page")]
    [InlineData("page=abc", "page")]
    [InlineData("page=1&page=2", "page")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("pageSize=ten", "pageSize")]
    [InlineData("status=Frozen", "status")]
    [InlineData("status=Active&status=Disabled", "status")]
    public async Task List_InvalidQuery_Is400ValidationFailed_NamingTheParameter(string query, string parameter)
    {
        var (_, token) = await AdminAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/users?{query}", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
        Assert.True(response.Json.GetProperty("errors").TryGetProperty(parameter, out _));
    }

    [Fact]
    public async Task List_AcceptsTheMaximumPageSize_AndAnOverlongSearchIsRejected()
    {
        var (_, token) = await AdminAsync();

        var max = await SendAsync(HttpMethod.Get, "/api/v1/users?pageSize=100", token);
        var tooLong = await SendAsync(HttpMethod.Get, $"/api/v1/users?search={new string('a', 257)}", token);

        Assert.Equal(HttpStatusCode.OK, max.Status);
        Assert.Equal(100, max.Json.GetProperty("pageSize").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.Status);
    }

    [Fact]
    public async Task List_ASearchContainingNul_Is400ValidationFailed_NotAServerError()
    {
        var (_, token) = await AdminAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/users?search=a%00b", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
        Assert.True(response.Json.GetProperty("errors").TryGetProperty("search", out _));
    }

    [Fact]
    public async Task Create_ALoginIdentifierContainingNul_Is400LoginIdentifierInvalid_NotAServerError()
    {
        var (_, token) = await AdminAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/users", token,
            rawBody: "{\"loginIdentifier\":\"a\\u0000b\",\"roleCodes\":[\"OfficeStaff\"]}");
        var role = await SendAsync(HttpMethod.Post, "/api/v1/users", token,
            rawBody: "{\"loginIdentifier\":\"nul.role\",\"roleCodes\":[\"Office\\u0000Staff\"]}");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("users.login_identifier_invalid", response.Code);
        Assert.Equal(HttpStatusCode.BadRequest, role.Status);
        Assert.Equal("users.role_unknown", role.Code);
    }

    [Fact]
    public async Task Get_ReturnsTheUserObject_AndTheBootstrapAdminIsFlagged()
    {
        var (_, token) = await AdminAsync();
        var bootstrap = await BootstrapAdminAsync();
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);

        var plain = await SendAsync(HttpMethod.Get, $"/api/v1/users/{user.Id}", token);
        var boot = await SendAsync(HttpMethod.Get, $"/api/v1/users/{bootstrap.Id}", token);

        Assert.Equal(HttpStatusCode.OK, plain.Status);
        Assert.Equal(user.Id.ToString(), plain.String("id"));
        Assert.Equal(user.Login, plain.String("loginIdentifier"));
        Assert.Equal("Active", plain.String("status"));
        Assert.Equal("PasswordSet", plain.String("credentialState"));
        Assert.True(boot.Json.GetProperty("isBootstrapAdmin").GetBoolean());
        Assert.Equal("PasswordSet", boot.String("credentialState"));
        Assert.Equal(
            ["id", "loginIdentifier", "status", "roles", "isBootstrapAdmin", "credentialState"],
            plain.Json.EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("not-a-guid")]
    public async Task Get_AnUnknownUser_Is404NotFound(string id)
    {
        var (_, token) = await AdminAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/users/{id}", token);

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal("users.not_found", response.Code);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("reactivate")]
    [InlineData("reset-password")]
    [InlineData("initial-password")]
    public async Task ActionsOnAnUnknownOrMalformedId_Are404NotFound(string action)
    {
        var (_, token) = await AdminAsync();

        var unknown = await SendAsync(HttpMethod.Post, $"/api/v1/users/{Guid.NewGuid()}/{action}", token);
        var malformed = await SendAsync(HttpMethod.Post, $"/api/v1/users/abc/{action}", token);

        Assert.All([unknown, malformed], r =>
        {
            Assert.Equal(HttpStatusCode.NotFound, r.Status);
            Assert.Equal("users.not_found", r.Code);
        });
    }

    // ---- disable / reactivate - §9.4, §9.5 -------------------------------------------

    [Fact]
    public async Task Disable_Is200WithTheUser_RevokesSessions_BumpsTheSecurityVersion_AndAudits()
    {
        var (admin, token) = await AdminAsync();
        var target = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var session = await Calls.LoginAsync(_client, target.Login, target.Password);
        var versionBefore = (await Seed.UserByIdAsync(_database, target.Id)).SecurityVersion;

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/users/{target.Id}/disable", token);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Disabled", response.String("status"));
        Assert.Equal(target.Id.ToString(), response.String("id"));
        Assert.All(await Seed.SessionsOfAsync(_database, target.Id), s => Assert.NotNull(s.RevokedAt));
        Assert.True((await Seed.UserByIdAsync(_database, target.Id)).SecurityVersion > versionBefore);
        var audit = Assert.Single(await AuditOfAsync(admin.Id, AuditActionType.UserDisabled));
        Assert.Equal("Active", audit.PreviousStatus);
        Assert.Equal("Disabled", audit.NewStatus);
        Assert.Equal(AuditSource.Web, audit.Source);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.MeAsync(_client, session.String("accessToken")!)).Status);

        var again = await SendAsync(HttpMethod.Post, $"/api/v1/users/{target.Id}/disable", token);
        Assert.Equal(HttpStatusCode.Conflict, again.Status);
        Assert.Equal("users.already_disabled", again.Code);
    }

    [Fact]
    public async Task Reactivate_Is200WithTheUser_RestoresNoSession_AndAudits()
    {
        var (admin, token) = await AdminAsync();
        var target = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var session = await Calls.LoginAsync(_client, target.Login, target.Password);
        await SendAsync(HttpMethod.Post, $"/api/v1/users/{target.Id}/disable", token);

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/users/{target.Id}/reactivate", token);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("Active", response.String("status"));
        Assert.All(await Seed.SessionsOfAsync(_database, target.Id), s => Assert.NotNull(s.RevokedAt)); // R25: nothing restored
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.MeAsync(_client, session.String("accessToken")!)).Status);
        var audit = Assert.Single(await AuditOfAsync(admin.Id, AuditActionType.UserReactivated));
        Assert.Equal("Disabled", audit.PreviousStatus);
        Assert.Equal("Active", audit.NewStatus);
        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, target.Login, target.Password)).Status);

        var again = await SendAsync(HttpMethod.Post, $"/api/v1/users/{target.Id}/reactivate", token);
        Assert.Equal(HttpStatusCode.Conflict, again.Status);
        Assert.Equal("users.already_active", again.Code);
    }

    [Fact]
    public async Task Disable_ConcurrentRequests_OneWins_TheOtherIs409AlreadyDisabled()
    {
        var (_, token) = await AdminAsync();
        var target = await Seed.UserAsync(_database, Now, ["Engineer"]);

        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            SendAsync(HttpMethod.Post, $"/api/v1/users/{target.Id}/disable", token)));

        Assert.Equal(1, responses.Count(r => r.Status == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.Status != HttpStatusCode.OK), r =>
        {
            Assert.Equal(HttpStatusCode.Conflict, r.Status);
            Assert.Equal("users.already_disabled", r.Code);
        });
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("reactivate")]
    [InlineData("reset-password")]
    [InlineData("initial-password")]
    public async Task TheBootstrapAdmin_IsProtectedFromEveryMutation_NothingChangesAndNothingIsAudited(string action)
    {
        var (admin, token) = await AdminAsync();
        var bootstrap = await BootstrapAdminAsync();
        var before = await Seed.UserByIdAsync(_database, bootstrap.Id);

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/users/{bootstrap.Id}/{action}", token);

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal("users.bootstrap_account_protected", response.Code);
        var after = await Seed.UserByIdAsync(_database, bootstrap.Id);
        Assert.Equal(before.SecurityVersion, after.SecurityVersion);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.DoesNotContain(await Seed.AuditEventsAsync(_database, admin.Id), e => e.EntityId == bootstrap.Id);
        Assert.True(_factory.Logs.Contains("Users.BootstrapAccountProtected"));
        Assert.DoesNotContain(await Seed.AuditEventsAsync(_database), e => e.ActionType == AuditActionType.AccessDenied && e.ActorUserId == admin.Id);
    }

    // ---- reset-password - §9.6 -------------------------------------------------------

    [Fact]
    public async Task ResetPassword_WithNoDeliveryChannelConfigured_Is503_AndNothingChanges()
    {
        await using var plain = new ApiFactory(_database); // the shipped state: no IPasswordResetDelivery registered
        var client = plain.CreateApiClient();
        var admin = await Seed.UserAsync(_database, plain.Clock.UtcNow, ["Admin"]);
        var token = (await Calls.LoginAsync(client, admin.Login, admin.Password, "admin")).String("accessToken")!;
        var target = await Seed.UserAsync(_database, plain.Clock.UtcNow, ["Engineer"]);
        var session = await Calls.LoginAsync(client, target.Login, target.Password);
        var before = await Seed.UserByIdAsync(_database, target.Id);

        var response = await Calls.SendAsync(client, HttpMethod.Post, $"/api/v1/users/{target.Id}/reset-password", bearer: token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        Assert.Equal("users.reset_delivery_unavailable", response.Code);
        var after = await Seed.UserByIdAsync(_database, target.Id);
        Assert.Equal(before.SecurityVersion, after.SecurityVersion);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.All(await Seed.SessionsOfAsync(_database, target.Id), s => Assert.Null(s.RevokedAt));
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(client, session.String("accessToken")!)).Status);
        Assert.Empty(await AuditOfAsync(admin.Id, AuditActionType.PasswordResetIssued));
        await using (var db = _database.CreateDbContext())
        {
            Assert.False(await db.PasswordResetTickets.AnyAsync(t => t.UserId == target.Id));
        }

        Assert.True(plain.Logs.Contains("Users.ResetDeliveryUnavailable"));
    }

    [Fact]
    public async Task ResetPassword_WhenTheChannelCannotReachTheUser_Is503()
    {
        var (_, token) = await AdminAsync();
        var target = await Seed.UserAsync(_database, Now, ["Engineer"]);
        _delivery.Reachable = false;

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/users/{target.Id}/reset-password", token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        Assert.Equal("users.reset_delivery_unavailable", response.Code);
    }

    [Fact]
    public async Task ResetPassword_Is202WithoutAnySecret_InvalidatesThePassword_RevokesSessions_AuditsAndDeliversTheCode()
    {
        var (admin, token) = await AdminAsync();
        var target = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var session = await Calls.LoginAsync(_client, target.Login, target.Password);

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/users/{target.Id}/reset-password", token);

        Assert.Equal(HttpStatusCode.Accepted, response.Status);
        Assert.Equal("test-channel", response.String("channel"));
        Assert.Equal(Now.AddHours(24).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), response.String("expiresAt"));
        Assert.Equal(["channel", "expiresAt"], response.Json.EnumerateObject().Select(p => p.Name));
        Assert.Equal("no-store", response.Header("Cache-Control"));
        var (deliveredTo, code) = Assert.Single(_delivery.Sent);
        Assert.Equal(target.Id, deliveredTo);
        Assert.DoesNotContain(code, response.AllText);
        Assert.DoesNotContain(code, await Seed.AuditTableTextAsync(_database));
        Assert.DoesNotContain(code, _factory.Logs.AllText);

        Assert.All(await Seed.SessionsOfAsync(_database, target.Id), s => Assert.NotNull(s.RevokedAt));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.LoginAsync(_client, target.Login, target.Password)).Status);
        var audit = Assert.Single(await AuditOfAsync(admin.Id, AuditActionType.PasswordResetIssued));
        Assert.Equal(target.Id, audit.EntityId);
        Assert.Equal(AuditSource.Web, audit.Source);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.MeAsync(_client, session.String("accessToken")!)).Status);

        var read = await SendAsync(HttpMethod.Get, $"/api/v1/users/{target.Id}", token);
        Assert.Equal("ResetPending", read.String("credentialState"));
    }

    [Fact]
    public async Task ResetPassword_CredentialStateFollowsTheTicket_AndTheUserCanCompleteTheReset()
    {
        var (_, token) = await AdminAsync();
        var target = await Seed.UserAsync(_database, Now, ["Engineer"]);
        await SendAsync(HttpMethod.Post, $"/api/v1/users/{target.Id}/reset-password", token);
        var code = _delivery.Sent.Single().Code;

        // The code expires after 24 hours, unused: ResetRequired (the Admin token is re-issued first).
        _factory.Clock.Advance(TimeSpan.FromHours(25));
        var (_, laterToken) = await AdminAsync();
        var expired = await SendAsync(HttpMethod.Get, $"/api/v1/users/{target.Id}", laterToken);
        Assert.Equal("ResetRequired", expired.String("credentialState"));

        // Issuing again supersedes the old ticket: ResetPending, and only the new code works.
        await SendAsync(HttpMethod.Post, $"/api/v1/users/{target.Id}/reset-password", laterToken);
        Assert.Equal("ResetPending", (await SendAsync(HttpMethod.Get, $"/api/v1/users/{target.Id}", laterToken)).String("credentialState"));
        var newCode = _delivery.Sent.Last().Code;
        Assert.NotEqual(code, newCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Calls.CompleteResetAsync(_client, target.Login, code, "Chosen-Passw0rd")).Status);

        var completed = await Calls.CompleteResetAsync(_client, target.Login, newCode, "Chosen-Passw0rd");
        Assert.Equal(HttpStatusCode.NoContent, completed.Status);
        Assert.Equal("PasswordSet", (await SendAsync(HttpMethod.Get, $"/api/v1/users/{target.Id}", laterToken)).String("credentialState"));
    }

    [Fact]
    public async Task ResetPassword_APostCommitDeliveryFailure_StillLeavesAValidTicket()
    {
        // PROVISIONAL (contract gap): §9.6 step 6 defines no HTTP outcome for this; 202 is returned.
        var (admin, token) = await AdminAsync();
        var target = await Seed.UserAsync(_database, Now, ["Engineer"]);
        _delivery.ThrowOnSend = true;

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/users/{target.Id}/reset-password", token);

        Assert.Equal(HttpStatusCode.Accepted, response.Status);
        Assert.Single(await AuditOfAsync(admin.Id, AuditActionType.PasswordResetIssued));
        Assert.True(_factory.Logs.Contains("delivery failed"));
        Assert.Equal("ResetPending", (await SendAsync(HttpMethod.Get, $"/api/v1/users/{target.Id}", token)).String("credentialState"));
    }

    [Fact]
    public async Task ResetPassword_Preconditions_AreConflicts()
    {
        var (_, token) = await AdminAsync();
        var disabled = await Seed.UserAsync(_database, Now, ["Engineer"], disabled: true);
        var firstPassword = await Seed.UserAsync(_database, Now, ["Engineer"], mustChangePassword: true);

        var onDisabled = await SendAsync(HttpMethod.Post, $"/api/v1/users/{disabled.Id}/reset-password", token);
        var onFirst = await SendAsync(HttpMethod.Post, $"/api/v1/users/{firstPassword.Id}/reset-password", token);

        Assert.Equal(HttpStatusCode.Conflict, onDisabled.Status);
        Assert.Equal("users.already_disabled", onDisabled.Code);
        Assert.Equal(HttpStatusCode.Conflict, onFirst.Status);
        Assert.Equal("users.initial_password_pending", onFirst.Code);
        Assert.Empty(_delivery.Sent);
    }

    // ---- initial-password - §9.8 -----------------------------------------------------

    [Fact]
    public async Task InitialPassword_ForAnExpiredTemporaryPassword_Is200WithANewOne_NoStore_AndAudits()
    {
        var (admin, token) = await AdminAsync();
        var target = await Seed.UserAsync(_database, Now, ["Engineer"], mustChangePassword: true, temporaryPasswordExpiresAt: Now.AddHours(-1));
        Assert.Equal("InitialPasswordRequired", (await SendAsync(HttpMethod.Get, $"/api/v1/users/{target.Id}", token)).String("credentialState"));

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/users/{target.Id}/initial-password", token);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("no-store", response.Header("Cache-Control"));
        Assert.Equal(["temporaryPassword", "temporaryPasswordExpiresAt"], response.Json.EnumerateObject().Select(p => p.Name));
        var temporaryPassword = response.String("temporaryPassword")!;
        Assert.Equal(12, temporaryPassword.Length);
        Assert.Equal(Now.AddHours(72).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), response.String("temporaryPasswordExpiresAt"));
        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, target.Login, temporaryPassword)).Status);
        Assert.Single(await AuditOfAsync(admin.Id, AuditActionType.InitialPasswordReissued));
        Assert.DoesNotContain(temporaryPassword, await Seed.AuditTableTextAsync(_database));
        Assert.DoesNotContain(temporaryPassword, _factory.Logs.AllText);
        Assert.Equal("TemporaryPasswordIssued", (await SendAsync(HttpMethod.Get, $"/api/v1/users/{target.Id}", token)).String("credentialState"));
    }

    [Fact]
    public async Task InitialPassword_Preconditions_AreConflicts()
    {
        var (_, token) = await AdminAsync();
        var chosen = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var disabled = await Seed.UserAsync(_database, Now, ["Engineer"], mustChangePassword: true, disabled: true);

        var onChosen = await SendAsync(HttpMethod.Post, $"/api/v1/users/{chosen.Id}/initial-password", token);
        var onDisabled = await SendAsync(HttpMethod.Post, $"/api/v1/users/{disabled.Id}/initial-password", token);

        Assert.Equal(HttpStatusCode.Conflict, onChosen.Status);
        Assert.Equal("users.initial_password_not_reissuable", onChosen.Code);
        Assert.Equal(HttpStatusCode.Conflict, onDisabled.Status);
        Assert.Equal("users.already_disabled", onDisabled.Code);
        Assert.DoesNotContain("temporaryPassword", onChosen.Body);
    }

    // ---- GET /roles - §9.7 -----------------------------------------------------------

    [Fact]
    public async Task Roles_ListsTheActiveRoleCodesSortedByCode_AndNothingElse()
    {
        var (_, token) = await AdminAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/roles", token);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(JsonValueKind.Array, response.Json.ValueKind);
        Assert.Equal(["Accountant", "Admin", "Engineer", "OfficeStaff"], response.Json.EnumerateArray().Select(r => r.GetProperty("code").GetString()));
        Assert.All(response.Json.EnumerateArray(), r => Assert.Single(r.EnumerateObject()));
    }

    // ---- error shape -------------------------------------------------------------------

    [Fact]
    public async Task EveryProblem_CarriesTheCodeTheCorrelationIdAndNoDetail()
    {
        var (_, token) = await AdminAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/users/{Guid.NewGuid()}", token);

        Assert.Equal("application/problem+json", response.Message.Content.Headers.ContentType!.MediaType);
        Assert.Equal(404, response.Json.GetProperty("status").GetInt32());
        Assert.Equal("users.not_found", response.Code);
        Assert.False(string.IsNullOrEmpty(response.String("correlationId")));
        Assert.False(response.Json.TryGetProperty("detail", out _));
    }
}
