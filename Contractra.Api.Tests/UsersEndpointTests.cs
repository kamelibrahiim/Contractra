using System.Net;
using System.Text.Json;
using Contractra.Api.Integrations;
using Contractra.Api.Tests.Support;
using Contractra.Application.Identity.Credentials;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Domain.Parties;
using Contractra.Infrastructure.Identity;
using Contractra.Infrastructure.IntegrationTests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contractra.Api.Tests;

/// <summary>
/// /api/v1/users and /api/v1/roles - contract v1.5 §9 as amended by v1.6 §23 (Backend Sprint 2 Slice 7; PS-04a): the §9
/// check order, the response contracts, the derived credentialState, the credential issuance (one generated password
/// per operation, delivered by the worker, never in a response) and the audit rows, against an ephemeral PostgreSQL
/// database.
/// </summary>
public sealed class UsersEndpointTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private const string Number = "+201000000001";

    private readonly PostgresTestDatabaseFixture _database;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public UsersEndpointTests(PostgresTestDatabaseFixture database)
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

    /// <summary>The fake sender behind the real worker (registered because WhatsApp:Sender = Fake in Development).</summary>
    private FakeWhatsAppSender Sender => _factory.Services.GetRequiredService<FakeWhatsAppSender>();

    /// <summary>One worker cycle: sweep, then attempt every due issuance (the timer is parked in tests).</summary>
    private Task RunWorkerAsync() => _factory.Services.GetRequiredService<CredentialDeliveryWorker>().RunOnceAsync(CancellationToken.None);

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
        { "GET", "/api/v1/users/{id}/credential-delivery" },
        { "POST", "/api/v1/users/username-suggestions" },
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

    // ---- POST /users - §9.1 as replaced by §23.2 ---------------------------------------

    /// <summary>A create body in the v1.6 shape: roles, an optional party, and the WhatsApp number with the consent affirmation.</summary>
    private static object Body(
        string? login = null, string[]? roles = null, Guid? partyId = null, string? number = Number, bool consent = true, string basis = "verbal") =>
        new
        {
            loginIdentifier = login ?? Seed.NewPolicyLogin(),
            roleCodes = roles ?? ["OfficeStaff"],
            partyId,
            whatsapp = new { number, consent = new { affirmed = consent, basis } },
        };

    [Fact]
    public async Task Create_AnEngineerWithAParty_Is201WithTheContractBody_AndNeverReturnsAPassword()
    {
        var (admin, token) = await AdminAsync();
        var party = await SeedPartyAsync();
        var login = Seed.NewPolicyLogin("eng");

        var response = await CreateAsync(token, Body(login, ["engineer"], party.Id));

        Assert.Equal(HttpStatusCode.Created, response.Status);
        var user = response.Json.GetProperty("user");
        var id = Guid.Parse(user.GetProperty("id").GetString()!);
        Assert.Equal($"/api/v1/users/{id}", response.Header("Location"));
        Assert.Equal("no-store", response.Header("Cache-Control"));
        Assert.Equal(login, user.GetProperty("loginIdentifier").GetString());
        Assert.Equal("Active", user.GetProperty("status").GetString());
        Assert.Equal(["Engineer"], Roles(user)); // canonical casing, not the requested one
        Assert.False(user.GetProperty("isBootstrapAdmin").GetBoolean());
        Assert.Equal("DeliveryPending", user.GetProperty("credentialState").GetString());
        Assert.Equal(["user", "credentialDelivery"], response.Json.EnumerateObject().Select(p => p.Name));

        var delivery = response.Json.GetProperty("credentialDelivery");
        Assert.Equal(
            ["issuanceId", "kind", "status", "attempts", "updatedAt", "destinationMasked", "failureCategory", "stalled"],
            delivery.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Initial", delivery.GetProperty("kind").GetString());
        Assert.Equal("Queued", delivery.GetProperty("status").GetString());
        Assert.Equal(0, delivery.GetProperty("attempts").GetInt32());
        Assert.Equal("+20••••••••01", delivery.GetProperty("destinationMasked").GetString());
        Assert.Equal(JsonValueKind.Null, delivery.GetProperty("failureCategory").ValueKind);
        Assert.False(delivery.GetProperty("stalled").GetBoolean());

        // R51: nothing in the response can be a password, a hash, or the full number.
        Assert.DoesNotContain("password", response.AllText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Number, response.AllText);
        Assert.DoesNotContain("1000000001", response.AllText);

        var stored = await Seed.UserByIdAsync(_database, id);
        Assert.False(stored.MustChangePassword); // R49: no forced change
        Assert.Null(stored.TemporaryPasswordExpiresAt);
        await using (var db = _database.CreateDbContext())
        {
            Assert.Equal(party.Id, (await db.UserPartyLinks.SingleAsync(l => l.UserId == id)).PartyId);
        }

        var events = await Seed.AuditEventsAsync(_database, admin.Id);
        var created = Assert.Single(events, e => e.ActionType == AuditActionType.UserCreated && e.EntityId == id);
        Assert.Equal(AuditSource.Web, created.Source);
        Assert.Equal("Admin", created.ActorRoleCode);
        Assert.Equal(response.Header("X-Correlation-ID"), created.CorrelationId);
        Assert.Contains(delivery.GetProperty("issuanceId").GetString()!, created.Snapshot!);
        Assert.Single(events, e => e.ActionType == AuditActionType.RoleAssigned && e.EntityId == id);
        Assert.DoesNotContain(Sender.Sent, m => m.Username == login); // creation itself sends nothing
    }

    [Fact]
    public async Task Create_ThenTheWorkerDelivers_TheSamePasswordTheUserSignsInWith_AndItAppearsNowhereElse()
    {
        var (_, token) = await AdminAsync();
        var login = Seed.NewPolicyLogin("deliver");
        var created = await CreateAsync(token, Body(login));
        var id = created.Json.GetProperty("user").GetProperty("id").GetString()!;

        await RunWorkerAsync();

        var message = Assert.Single(Sender.Sent, m => m.Username == login);
        Assert.Equal(Number, message.Destination);
        Assert.Equal(12, message.Password.Length);

        // The employee signs in with it - no forced change (R49) - and the delivery is now Accepted.
        var signIn = await Calls.LoginAsync(_client, login, message.Password, "admin");
        Assert.Equal(HttpStatusCode.OK, signIn.Status);
        Assert.False(signIn.Json.GetProperty("mustChangePassword").GetBoolean());

        var status = await SendAsync(HttpMethod.Get, $"/api/v1/users/{id}/credential-delivery", token);
        Assert.Equal(HttpStatusCode.OK, status.Status);
        Assert.Equal("Accepted", status.Json.GetProperty("credentialDelivery").GetProperty("status").GetString());
        Assert.Equal(1, status.Json.GetProperty("credentialDelivery").GetProperty("attempts").GetInt32());

        Assert.DoesNotContain(message.Password, status.AllText);
        Assert.DoesNotContain(message.Password, await Seed.AuditTableTextAsync(_database));
        Assert.DoesNotContain(message.Password, _factory.Logs.AllText);
        Assert.DoesNotContain(Number, _factory.Logs.AllText);
    }

    [Fact]
    public async Task Create_ANonEngineerWithoutAParty_Is201()
    {
        var (_, token) = await AdminAsync();

        var response = await CreateAsync(token, Body(roles: ["OfficeStaff", "Accountant"]));

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
        "{\"loginIdentifier\":\"x5\",\"roleCodes\":[\"OfficeStaff\"],\"whatsapp\":\"+201000000001\"}",
        "{\"loginIdentifier\":\"x6\",\"roleCodes\":[\"OfficeStaff\"],\"whatsapp\":{\"number\":5}}",
        "{\"loginIdentifier\":\"x7\",\"roleCodes\":[\"OfficeStaff\"],\"whatsapp\":{\"number\":\"+201000000001\",\"consent\":{\"affirmed\":\"yes\"}}}",
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
        var number = await SendAsync(HttpMethod.Post, "/api/v1/users", token, rawBody: "{\"loginIdentifier\":\"x\",\"roleCodes\":[\"OfficeStaff\"],\"whatsapp\":{\"number\":5}}");

        Assert.True(roles.Json.GetProperty("errors").TryGetProperty("roleCodes", out _));
        Assert.True(party.Json.GetProperty("errors").TryGetProperty("partyId", out _));
        Assert.True(number.Json.GetProperty("errors").TryGetProperty("whatsapp.number", out _));
    }

    [Fact]
    public async Task Create_DuplicateRoleCodes_AndAPartyForANonEngineer_AreValidationFailed()
    {
        var (_, token) = await AdminAsync();
        var party = await SeedPartyAsync();

        var duplicate = await CreateAsync(token, Body(roles: ["OfficeStaff", "officestaff"]));
        var stray = await CreateAsync(token, Body(roles: ["OfficeStaff"], partyId: party.Id));

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

        var invalidLogin = await Post(Body(login: " padded "));
        var emptyLogin = await SendAsync(HttpMethod.Post, "/api/v1/users", token, rawBody: "{\"roleCodes\":[\"OfficeStaff\"]}");
        var tooLong = await Post(Body(login: new string('a', 257)));
        var noRoles = await Post(Body(roles: []));
        var absentRoles = await SendAsync(HttpMethod.Post, "/api/v1/users", token, rawBody: "{\"loginIdentifier\":\"abcd\"}");
        var unknownRole = await Post(Body(roles: ["Wizard"]));
        var noParty = await Post(Body(roles: ["Engineer"]));
        var missingParty = await Post(Body(roles: ["Engineer"], partyId: Guid.NewGuid()));
        var notActive = await Post(Body(roles: ["Engineer"], partyId: inactive.Id));
        var notClassified = await Post(Body(roles: ["Engineer"], partyId: needsClassification.Id));
        var noEngineerRole = await Post(Body(roles: ["Engineer"], partyId: noRole.Id));

        Assert.All([invalidLogin, emptyLogin, tooLong], r => Assert.Equal("users.login_identifier_invalid", r.Code));
        Assert.Equal("whitespace", invalidLogin.String("reason"));
        Assert.Equal("too_short", emptyLogin.String("reason"));
        Assert.Equal("too_long", tooLong.String("reason"));
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
        Assert.Null(noParty.String("reason"));
    }

    [Theory]
    [InlineData("admin", "reserved")]
    [InlineData("Admin2", "reserved")]
    [InlineData("OfficeStaff", "reserved")] // a role code
    [InlineData("ab", "too_short")]
    [InlineData("1abc", "bad_shape")]
    [InlineData("has space", "whitespace")]
    [InlineData("a@b.com", "bad_characters")]
    [InlineData("user1234567", "phone_like")]
    public async Task Create_TheUsernamePolicyRefusesAnInvalidIdentifier_WithTheReason(string login, string reason)
    {
        var (_, token) = await AdminAsync();

        var response = await CreateAsync(token, Body(login: login));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("users.login_identifier_invalid", response.Code);
        Assert.Equal(reason, response.String("reason"));
    }

    [Theory]
    [InlineData("01000000001")]
    [InlineData("+0123456789")]
    [InlineData("not a number")]
    public async Task Create_AnInvalidWhatsAppNumber_Is400_AndCreatesNothing(string number)
    {
        var (_, token) = await AdminAsync();
        var before = await CountUsersAsync();

        var response = await CreateAsync(token, Body(number: number));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("users.whatsapp_number_invalid", response.Code);
        Assert.Equal(before, await CountUsersAsync());
    }

    [Fact]
    public async Task Create_WithoutTheNumberOrWithoutAffirmedConsent_Is400_AndCreatesNothing()
    {
        var (_, token) = await AdminAsync();
        var before = await CountUsersAsync();

        var noNumber = await SendAsync(HttpMethod.Post, "/api/v1/users", token, rawBody: $"{{\"loginIdentifier\":\"{Seed.NewPolicyLogin()}\",\"roleCodes\":[\"OfficeStaff\"]}}");
        var noConsent = await CreateAsync(token, Body(consent: false));
        var badBasis = await CreateAsync(token, Body(basis: "telepathy"));

        Assert.Equal("users.whatsapp_number_invalid", noNumber.Code);
        Assert.Equal("users.whatsapp_consent_required", noConsent.Code);
        Assert.Equal("users.whatsapp_consent_required", badBasis.Code);
        Assert.All([noNumber, noConsent, badBasis], r => Assert.Equal(HttpStatusCode.BadRequest, r.Status));
        Assert.Equal(before, await CountUsersAsync());
    }

    [Fact]
    public async Task Create_AnIdentifierIsStoredInItsCanonicalLowercaseForm()
    {
        var (_, token) = await AdminAsync();
        var login = Seed.NewPolicyLogin("mixed");

        var response = await CreateAsync(token, Body(login: login.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.Equal(login, response.Json.GetProperty("user").GetProperty("loginIdentifier").GetString());
    }

    [Fact]
    public async Task Create_ADuplicateLoginIdentifier_IsCaseInsensitive409WithFreeAlternatives_AndAPartyCanBeLinkedOnce()
    {
        var (_, token) = await AdminAsync();
        var party = await SeedPartyAsync();
        var login = Seed.NewPolicyLogin("dup");
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(token, Body(login, ["Engineer"], party.Id))).Status);

        var taken = await CreateAsync(token, Body(login.ToUpperInvariant()));
        var linked = await CreateAsync(token, Body(roles: ["Engineer"], partyId: party.Id));

        Assert.Equal(HttpStatusCode.Conflict, taken.Status);
        Assert.Equal("users.login_identifier_taken", taken.Code);
        var alternatives = taken.Json.GetProperty("alternatives").EnumerateArray().Select(a => a.GetString()!).ToList();
        Assert.Equal(3, alternatives.Count);
        Assert.Equal([login + "2", login + "3", login + "4"], alternatives);
        Assert.Equal(HttpStatusCode.Conflict, linked.Status);
        Assert.Equal("users.party_already_linked", linked.Code);
    }

    [Fact]
    public async Task Create_ConcurrentRequestsForTheSameLogin_OneWins_TheOtherIs409Taken()
    {
        var (_, token) = await AdminAsync();
        var body = Body(Seed.NewPolicyLogin("race"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CreateAsync(token, body)));

        Assert.Equal(1, responses.Count(r => r.Status == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.Status != HttpStatusCode.Created), r =>
        {
            Assert.Equal(HttpStatusCode.Conflict, r.Status);
            Assert.Equal("users.login_identifier_taken", r.Code);
        });
        // The losers queued no credential issuance: exactly one user and one issuance exist for the login.
        var winner = responses.Single(r => r.Status == HttpStatusCode.Created);
        var winnerId = Guid.Parse(winner.Json.GetProperty("user").GetProperty("id").GetString()!);
        await using var db = _database.CreateDbContext();
        Assert.Equal(1, await db.CredentialIssuances.CountAsync(i => i.UserId == winnerId));
    }

    [Fact]
    public async Task Create_ConcurrentRequestsForTheSameParty_OneWins_TheOtherIs409AlreadyLinked()
    {
        var (_, token) = await AdminAsync();
        var party = await SeedPartyAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            CreateAsync(token, Body(Seed.NewPolicyLogin("racep"), ["Engineer"], party.Id))));

        Assert.Equal(1, responses.Count(r => r.Status == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.Status != HttpStatusCode.Created), r =>
        {
            Assert.Equal(HttpStatusCode.Conflict, r.Status);
            Assert.Equal("users.party_already_linked", r.Code);
        });
    }

    [Fact]
    public async Task Create_WhenDeliveryIsNotAvailable_Is503_AndCreatesNothing()
    {
        foreach (var settings in new[]
        {
            new Dictionary<string, string?> { ["WhatsApp:Enabled"] = "false" },
            new Dictionary<string, string?> { ["WhatsApp:Sender"] = "None" },
            new Dictionary<string, string?> { ["CredentialSealing:ActiveKeyId"] = "" },
        })
        {
            await using var plain = new ApiFactory(_database, settings);
            var client = plain.CreateApiClient();
            var admin = await Seed.UserAsync(_database, plain.Clock.UtcNow, ["Admin"]);
            var token = (await Calls.LoginAsync(client, admin.Login, admin.Password, "admin")).String("accessToken")!;
            var before = await CountUsersAsync();

            var response = await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/users", Body(), token);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
            Assert.Equal("users.credential_delivery_unavailable", response.Code);
            Assert.Equal(before, await CountUsersAsync());
            Assert.True(plain.Logs.Contains("Users.CredentialDeliveryUnavailable"));
        }
    }

    private async Task<int> CountUsersAsync()
    {
        await using var db = _database.CreateDbContext();
        return await db.Users.CountAsync();
    }

    // ---- POST /users/username-suggestions - §23.5 --------------------------------------

    [Fact]
    public async Task UsernameSuggestions_SuggestFromTheNameWithTheFirstFreeSuffix()
    {
        var (_, token) = await AdminAsync();
        var given = "Zq" + Seed.NewLetters(8);
        var first = await SendAsync(HttpMethod.Post, "/api/v1/users/username-suggestions", token, new { name = $"{given} Hassan" });
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal("v1", first.String("policyVersion"));
        var suggestion = first.Json.GetProperty("suggestions")[0];
        Assert.Equal($"{given.ToLowerInvariant()}.hassan", suggestion.GetProperty("value").GetString());
        Assert.True(suggestion.GetProperty("available").GetBoolean());
        Assert.Equal("name", suggestion.GetProperty("source").GetString());

        // Once that username exists, the next free one is suggested.
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(token, Body(login: $"{given}.hassan"))).Status);
        var second = await SendAsync(HttpMethod.Post, "/api/v1/users/username-suggestions", token, new { name = $"{given} Hassan" });
        Assert.Equal($"{given.ToLowerInvariant()}.hassan2", second.Json.GetProperty("suggestions")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task UsernameSuggestions_ThePreferredLatinNameWins_AndAnUnusableNameGivesNoSuggestion()
    {
        var (_, token) = await AdminAsync();
        var given = "Zr" + Seed.NewLetters(8);

        var latin = await SendAsync(HttpMethod.Post, "/api/v1/users/username-suggestions", token, new { name = "أحمد محمد", latinName = $"{given} Mohammed" });
        var unusable = await SendAsync(HttpMethod.Post, "/api/v1/users/username-suggestions", token, new { name = "???" });

        Assert.Equal($"{given.ToLowerInvariant()}.mohammed", latin.Json.GetProperty("suggestions")[0].GetProperty("value").GetString());
        Assert.Equal("latinName", latin.Json.GetProperty("suggestions")[0].GetProperty("source").GetString());
        Assert.Equal(HttpStatusCode.OK, unusable.Status);
        Assert.Empty(unusable.Json.GetProperty("suggestions").EnumerateArray());
        Assert.Equal("name_unusable", unusable.String("reason"));
    }

    [Fact]
    public async Task UsernameSuggestions_ACandidateIsValidatedAndCheckedForAvailability()
    {
        var (_, token) = await AdminAsync();
        var login = Seed.NewPolicyLogin("cand");
        await CreateAsync(token, Body(login: login));

        var taken = await SendAsync(HttpMethod.Post, "/api/v1/users/username-suggestions", token, new { candidate = login });
        var reserved = await SendAsync(HttpMethod.Post, "/api/v1/users/username-suggestions", token, new { candidate = "admin" });
        var bad = await SendAsync(HttpMethod.Post, "/api/v1/users/username-suggestions", token, new { candidate = "a b" });

        Assert.False(taken.Json.GetProperty("suggestions")[0].GetProperty("available").GetBoolean());
        Assert.Equal(3, taken.Json.GetProperty("alternatives").GetArrayLength());
        Assert.Equal("reserved", reserved.String("reason"));
        Assert.Equal("whitespace", bad.String("reason"));
    }

    [Fact]
    public async Task UsernameSuggestions_AWrongShapeIs400()
    {
        var (_, token) = await AdminAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/users/username-suggestions", token, rawBody: "{\"name\":5}");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
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

        const string Whatsapp = "\"whatsapp\":{\"number\":\"+201000000001\",\"consent\":{\"affirmed\":true,\"basis\":\"verbal\"}}";
        var response = await SendAsync(HttpMethod.Post, "/api/v1/users", token,
            rawBody: "{\"loginIdentifier\":\"a\\u0000b\",\"roleCodes\":[\"OfficeStaff\"]," + Whatsapp + "}");
        var role = await SendAsync(HttpMethod.Post, "/api/v1/users", token,
            rawBody: "{\"loginIdentifier\":\"nul.role\",\"roleCodes\":[\"Office\\u0000Staff\"]," + Whatsapp + "}");

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

    // ---- credential issuance helpers ---------------------------------------------------

    /// <summary>Creates an account through the API, runs the worker once and returns the id, login and delivered password.</summary>
    private async Task<(Guid Id, string Login, string Password, Guid IssuanceId)> CreateDeliveredAsync(string token, string[]? roles = null)
    {
        var login = Seed.NewPolicyLogin("acct");
        var created = await CreateAsync(token, Body(login, roles ?? ["OfficeStaff"]));
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var id = Guid.Parse(created.Json.GetProperty("user").GetProperty("id").GetString()!);
        var issuance = Guid.Parse(created.Json.GetProperty("credentialDelivery").GetProperty("issuanceId").GetString()!);

        await RunWorkerAsync();
        var password = Sender.Sent.Last(m => m.Username == login).Password;
        return (id, login, password, issuance);
    }

    private Task<ApiResponse> ResetAsync(string token, Guid id, Guid? expectedIssuanceId = null) =>
        SendAsync(HttpMethod.Post, $"/api/v1/users/{id}/reset-password", token, expectedIssuanceId is null ? null : new { expectedIssuanceId });

    // ---- reset-password - §9.6 as replaced by §23.3 -------------------------------------

    [Fact]
    public async Task ResetPassword_Is202WithTheDeliveryStatusOnly_InvalidatesThePasswordAndSessions_AndAudits()
    {
        var (admin, token) = await AdminAsync();
        var (id, login, oldPassword, issuance) = await CreateDeliveredAsync(token);
        var session = await Calls.LoginAsync(_client, login, oldPassword, "admin");
        Assert.Equal(HttpStatusCode.OK, session.Status);

        var response = await ResetAsync(token, id, issuance);

        Assert.Equal(HttpStatusCode.Accepted, response.Status);
        Assert.Equal("no-store", response.Header("Cache-Control"));
        Assert.Equal(["credentialDelivery"], response.Json.EnumerateObject().Select(p => p.Name));
        var delivery = response.Json.GetProperty("credentialDelivery");
        Assert.Equal("Reset", delivery.GetProperty("kind").GetString());
        Assert.Equal("Queued", delivery.GetProperty("status").GetString());
        Assert.NotEqual(issuance.ToString(), delivery.GetProperty("issuanceId").GetString());
        Assert.DoesNotContain("password", response.AllText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Number, response.AllText);

        // OD-29(a): the old password and every session are invalid from the request, not from delivery.
        Assert.All(await Seed.SessionsOfAsync(_database, id), s => Assert.NotNull(s.RevokedAt));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.LoginAsync(_client, login, oldPassword, "admin")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.MeAsync(_client, session.String("accessToken")!)).Status);
        Assert.Equal("DeliveryPending", (await SendAsync(HttpMethod.Get, $"/api/v1/users/{id}", token)).String("credentialState"));

        var audit = Assert.Single(await AuditOfAsync(admin.Id, AuditActionType.PasswordResetIssued));
        Assert.Equal(id, audit.EntityId);
        Assert.Equal(AuditSource.Web, audit.Source);
        Assert.Contains(delivery.GetProperty("issuanceId").GetString()!, audit.Snapshot!);
        Assert.Contains("whatsapp", audit.Snapshot!);
        Assert.DoesNotContain(Number, audit.Snapshot!);
    }

    [Fact]
    public async Task ResetPassword_TheWorkerDeliversTheNewPassword_NeverTheOldOne_AndItSignsIn()
    {
        var (_, token) = await AdminAsync();
        var (id, login, oldPassword, issuance) = await CreateDeliveredAsync(token);
        await ResetAsync(token, id, issuance);

        await RunWorkerAsync();

        var messages = Sender.Sent.Where(m => m.Username == login).ToList();
        Assert.Equal(2, messages.Count);
        Assert.NotEqual(oldPassword, messages[1].Password);
        var signIn = await Calls.LoginAsync(_client, login, messages[1].Password, "admin");
        Assert.Equal(HttpStatusCode.OK, signIn.Status);
        Assert.False(signIn.Json.GetProperty("mustChangePassword").GetBoolean());
        Assert.DoesNotContain(messages[1].Password, await Seed.AuditTableTextAsync(_database));
        Assert.DoesNotContain(messages[1].Password, _factory.Logs.AllText);
    }

    [Fact]
    public async Task ResetPassword_AStaleOrMissingExpectedIssuanceId_Is409_AndNothingChanges()
    {
        var (_, token) = await AdminAsync();
        var created = await CreateAsync(token, Body()); // still Queued: the latest issuance is open
        var id = Guid.Parse(created.Json.GetProperty("user").GetProperty("id").GetString()!);
        var before = await Seed.UserByIdAsync(_database, id);

        var missing = await ResetAsync(token, id);
        var stale = await ResetAsync(token, id, Guid.NewGuid());

        Assert.All([missing, stale], r =>
        {
            Assert.Equal(HttpStatusCode.Conflict, r.Status);
            Assert.Equal("users.issuance_changed", r.Code);
        });
        Assert.Equal(before.PasswordHash, (await Seed.UserByIdAsync(_database, id)).PasswordHash);
    }

    [Fact]
    public async Task ResetPassword_ADoubleClick_IssuesOnce()
    {
        var (_, token) = await AdminAsync();
        var (id, _, _, issuance) = await CreateDeliveredAsync(token);
        var first = await ResetAsync(token, id, issuance);
        var queued = Guid.Parse(first.Json.GetProperty("credentialDelivery").GetProperty("issuanceId").GetString()!);

        // Two more clicks from a stale screen that still shows the first issuance.
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => ResetAsync(token, id, issuance)));

        Assert.Equal(HttpStatusCode.Accepted, first.Status);
        Assert.All(responses, r =>
        {
            Assert.Equal(HttpStatusCode.Conflict, r.Status);
            Assert.Equal("users.issuance_changed", r.Code);
        });
        await using var db = _database.CreateDbContext();
        Assert.Equal(2, await db.CredentialIssuances.CountAsync(i => i.UserId == id));
        Assert.Equal(queued, (await db.CredentialIssuances.AsNoTracking().Where(i => i.UserId == id && i.Kind == CredentialIssuanceKind.Reset).SingleAsync()).Id);
    }

    [Fact]
    public async Task ResetPassword_ConcurrentRequestsNamingTheSameIssuance_OneWins()
    {
        var (_, token) = await AdminAsync();
        var (id, _, _, issuance) = await CreateDeliveredAsync(token);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ResetAsync(token, id, issuance)));

        Assert.Equal(1, responses.Count(r => r.Status == HttpStatusCode.Accepted));
        Assert.All(responses.Where(r => r.Status != HttpStatusCode.Accepted), r =>
        {
            Assert.Equal(HttpStatusCode.Conflict, r.Status);
            Assert.Equal("users.issuance_changed", r.Code);
        });
        await using var db = _database.CreateDbContext();
        Assert.Equal(1, await db.CredentialIssuances.CountAsync(i => i.UserId == id && i.Kind == CredentialIssuanceKind.Reset));
        Assert.Equal(1, await db.CredentialIssuances.CountAsync(i => i.UserId == id && i.Status == CredentialDeliveryStatus.Queued));
    }

    [Fact]
    public async Task ResetPassword_WithinTheMinimumInterval_Is429WithRetryAfter()
    {
        await using var slow = new ApiFactory(_database, new Dictionary<string, string?> { ["WhatsApp:MinReissueInterval"] = "00:10:00" });
        var client = slow.CreateApiClient();
        var admin = await Seed.UserAsync(_database, slow.Clock.UtcNow, ["Admin"]);
        var token = (await Calls.LoginAsync(client, admin.Login, admin.Password, "admin")).String("accessToken")!;
        var created = await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/users", Body(), token);
        var id = created.Json.GetProperty("user").GetProperty("id").GetString()!;
        var issuance = created.Json.GetProperty("credentialDelivery").GetProperty("issuanceId").GetString();

        var response = await Calls.SendAsync(client, HttpMethod.Post, $"/api/v1/users/{id}/reset-password", new { expectedIssuanceId = issuance }, token);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.Status);
        Assert.Equal("users.reissue_too_soon", response.Code);
        Assert.InRange(int.Parse(response.Header("Retry-After")!), 1, 600);
    }

    [Fact]
    public async Task ResetPassword_WhenDeliveryIsUnavailable_Is503_AndNothingChanges()
    {
        var (_, creatorToken) = await AdminAsync();
        var (id, login, password, _) = await CreateDeliveredAsync(creatorToken);
        var session = await Calls.LoginAsync(_client, login, password, "admin");
        var before = await Seed.UserByIdAsync(_database, id);

        await using var off = new ApiFactory(_database, new Dictionary<string, string?> { ["WhatsApp:Enabled"] = "false" });
        var client = off.CreateApiClient();
        var admin = await Seed.UserAsync(_database, off.Clock.UtcNow, ["Admin"]);
        var token = (await Calls.LoginAsync(client, admin.Login, admin.Password, "admin")).String("accessToken")!;

        var response = await Calls.SendAsync(client, HttpMethod.Post, $"/api/v1/users/{id}/reset-password", bearer: token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        Assert.Equal("users.credential_delivery_unavailable", response.Code);
        var after = await Seed.UserByIdAsync(_database, id);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.SecurityVersion, after.SecurityVersion);
        Assert.All(await Seed.SessionsOfAsync(_database, id), s => Assert.Null(s.RevokedAt));
        Assert.Equal(HttpStatusCode.OK, (await Calls.MeAsync(_client, session.String("accessToken")!)).Status);
        Assert.Empty(await AuditOfAsync(admin.Id, AuditActionType.PasswordResetIssued));
        Assert.True(off.Logs.Contains("Users.CredentialDeliveryUnavailable"));
    }

    [Fact]
    public async Task ResetPassword_Preconditions_AreConflicts()
    {
        var (admin, token) = await AdminAsync();
        var disabled = await Seed.UserAsync(_database, Now, ["Engineer"], disabled: true);
        var legacy = await Seed.UserAsync(_database, Now, ["Engineer"]); // seeded: never had a credential issuance, so no stored number

        var onDisabled = await ResetAsync(token, disabled.Id);
        var onLegacy = await ResetAsync(token, legacy.Id);
        var onSelf = await ResetAsync(token, admin.Id);

        Assert.Equal(HttpStatusCode.Conflict, onDisabled.Status);
        Assert.Equal("users.already_disabled", onDisabled.Code);
        Assert.Equal(HttpStatusCode.Conflict, onLegacy.Status);
        Assert.Equal("users.whatsapp_destination_missing", onLegacy.Code);
        Assert.Equal(HttpStatusCode.Conflict, onSelf.Status);
        Assert.Equal("users.self_reset_not_allowed", onSelf.Code);
        Assert.DoesNotContain(Sender.Sent, m => m.Username == legacy.Login);
    }

    [Fact]
    public async Task ResetPassword_TheRequestCannotNameADestination()
    {
        var (_, token) = await AdminAsync();
        var (id, login, _, issuance) = await CreateDeliveredAsync(token);

        // An extra "number" in the body is ignored: the stored number is the only destination.
        var response = await SendAsync(HttpMethod.Post, $"/api/v1/users/{id}/reset-password", token,
            rawBody: $"{{\"expectedIssuanceId\":\"{issuance}\",\"number\":\"+201999999999\",\"whatsapp\":{{\"number\":\"+201999999999\"}}}}");
        await RunWorkerAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.Status);
        Assert.All(Sender.Sent.Where(m => m.Username == login), m => Assert.Equal(Number, m.Destination));
    }

    [Fact]
    public async Task Disable_CancelsTheOpenCredentialIssuance()
    {
        var (_, token) = await AdminAsync();
        var created = await CreateAsync(token, Body());
        var id = created.Json.GetProperty("user").GetProperty("id").GetString()!;

        await SendAsync(HttpMethod.Post, $"/api/v1/users/{id}/disable", token);
        await RunWorkerAsync();

        var status = await SendAsync(HttpMethod.Get, $"/api/v1/users/{id}/credential-delivery", token);
        Assert.Equal("Cancelled", status.Json.GetProperty("credentialDelivery").GetProperty("status").GetString());
        Assert.DoesNotContain(Sender.Sent, m => m.Username == created.Json.GetProperty("user").GetProperty("loginIdentifier").GetString());
    }

    // ---- GET /users/{id}/credential-delivery - §23.4 ------------------------------------

    [Fact]
    public async Task CredentialDelivery_ShowsTheLatestIssuanceAndTheHistory_WithoutNumbersOrSecrets()
    {
        var (_, token) = await AdminAsync();
        var (id, _, password, issuance) = await CreateDeliveredAsync(token);
        await ResetAsync(token, id, issuance);

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/users/{id}/credential-delivery", token);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("no-store", response.Header("Cache-Control"));
        Assert.Equal("Reset", response.Json.GetProperty("credentialDelivery").GetProperty("kind").GetString());
        var history = response.Json.GetProperty("history");
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal("Initial", history[0].GetProperty("kind").GetString());
        Assert.Equal("Superseded", history[0].GetProperty("status").GetString());
        Assert.DoesNotContain(password, response.AllText);
        Assert.DoesNotContain(Number, response.AllText);
    }

    [Fact]
    public async Task CredentialDelivery_AnUnknownUserIs404NotFound_AndAnAccountWithoutAnIssuanceIs404DeliveryNotFound()
    {
        var (_, token) = await AdminAsync();
        var legacy = await Seed.UserAsync(_database, Now, ["Engineer"]);

        var unknown = await SendAsync(HttpMethod.Get, $"/api/v1/users/{Guid.NewGuid()}/credential-delivery", token);
        var none = await SendAsync(HttpMethod.Get, $"/api/v1/users/{legacy.Id}/credential-delivery", token);

        Assert.Equal("users.not_found", unknown.Code);
        Assert.Equal(HttpStatusCode.NotFound, none.Status);
        Assert.Equal("users.credential_delivery_not_found", none.Code);
    }

    [Fact]
    public async Task CredentialState_FollowsTheLatestIssuance_PendingFailedThenPasswordSet()
    {
        var (_, token) = await AdminAsync();
        Sender.Script(new WhatsAppSendResult(WhatsAppSendOutcome.Definitive, Category: CredentialFailureCategory.NotOnWhatsApp));
        var login = Seed.NewPolicyLogin("state");
        var created = await CreateAsync(token, Body(login));
        var id = Guid.Parse(created.Json.GetProperty("user").GetProperty("id").GetString()!);
        var issuance = created.Json.GetProperty("credentialDelivery").GetProperty("issuanceId").GetString();
        Assert.Equal("DeliveryPending", created.Json.GetProperty("user").GetProperty("credentialState").GetString());

        await RunWorkerAsync();
        var failed = await SendAsync(HttpMethod.Get, $"/api/v1/users/{id}", token);
        Assert.Equal("DeliveryFailed", failed.String("credentialState"));
        var failedStatus = await SendAsync(HttpMethod.Get, $"/api/v1/users/{id}/credential-delivery", token);
        Assert.Equal("not_on_whatsapp", failedStatus.Json.GetProperty("credentialDelivery").GetProperty("failureCategory").GetString());

        // R51: the account stays; re-issue (naming the failed issuance) starts a new operation.
        Assert.Equal(HttpStatusCode.Accepted, (await ResetAsync(token, id, Guid.Parse(issuance!))).Status);
        await RunWorkerAsync();
        var accepted = await SendAsync(HttpMethod.Get, $"/api/v1/users/{id}/credential-delivery", token);
        Assert.Equal("Accepted", accepted.Json.GetProperty("credentialDelivery").GetProperty("status").GetString());
        Assert.Equal("DeliveryPending", (await SendAsync(HttpMethod.Get, $"/api/v1/users/{id}", token)).String("credentialState"));
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
