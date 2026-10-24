using System.Net;
using Contractra.Api.Tests.Support;
using Contractra.Application.Identity.Auth;
using Contractra.Infrastructure.IntegrationTests;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contractra.Api.Tests;

/// <summary>
/// The HTTP security pipeline: correlation id (contract v1.5 §13), ProblemDetails (§12),
/// input limits (§1), HTTPS only and HSTS outside development (§1, §18), CORS (§8, §18).
/// </summary>
public sealed class PipelineTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private readonly PostgresTestDatabaseFixture _database;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public PipelineTests(PostgresTestDatabaseFixture database)
    {
        _database = database;
    }

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_database, new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://admin.example.com",
        });
        _client = _factory.CreateApiClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private Task<ApiResponse> LoginWithCorrelationAsync(string? correlationId) =>
        Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}",
            headers: correlationId is null ? null : new Dictionary<string, string> { ["X-Correlation-ID"] = correlationId });

    [Theory]
    [InlineData("a")]
    [InlineData("Client-ID_1.2")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123")] // 64
    public async Task ValidClientCorrelationId_IsUsed_InTheHeaderAndTheProblem(string value)
    {
        var response = await LoginWithCorrelationAsync(value);
        Assert.Equal(value, response.Header("X-Correlation-ID"));
        Assert.Equal(value, response.String("correlationId"));
    }

    [Theory]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234")] // 65
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("ümlaut")]
    public async Task InvalidClientCorrelationId_IsIgnored_AndAUuidIsGenerated_NeverRejected(string value)
    {
        var response = await LoginWithCorrelationAsync(value);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status); // the {} body, not the header
        var generated = response.Header("X-Correlation-ID");
        Assert.True(Guid.TryParse(generated, out _));
        Assert.Equal(generated, response.String("correlationId"));
        Assert.True(_factory.Logs.Contains("Http.CorrelationIdReplaced"));
        Assert.DoesNotContain(value, _factory.Logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryResponseCarriesACorrelationId_IncludingSuccessAnd404()
    {
        var user = await Seed.UserAsync(_database, _factory.Clock.UtcNow, ["Engineer"]);
        var ok = await Calls.LoginAsync(_client, user.Login, user.Password);
        var noContent = await Calls.LogoutAsync(_client, ok.String("accessToken")!);
        var notFound = await Calls.SendAsync(_client, HttpMethod.Get, "/api/v1/nothing-here");
        var missing = await LoginWithCorrelationAsync(null);

        foreach (var response in new[] { ok, noContent, notFound, missing })
        {
            Assert.True(Guid.TryParse(response.Header("X-Correlation-ID"), out _));
        }

        Assert.Equal(HttpStatusCode.NotFound, notFound.Status);
    }

    [Fact]
    public async Task WrongMethod_Is405_NotAnAuthFailure_AndNotAudited()
    {
        var user = await Seed.UserAsync(_database, _factory.Clock.UtcNow, ["Engineer"]);
        var token = (await Calls.LoginAsync(_client, user.Login, user.Password)).String("accessToken");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Calls.SendAsync(_client, HttpMethod.Get, "/api/v1/auth/login")).Status);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Calls.SendAsync(_client, HttpMethod.Get, "/api/v1/auth/login", bearer: token)).Status);
        Assert.Empty(await Seed.AuditEventsAsync(_database, user.Id));
    }

    [Fact]
    public async Task ProblemDetails_HasTheContractShape()
    {
        var response = await LoginWithCorrelationAsync("shape-check");

        Assert.Equal("application/problem+json", response.Message.Content.Headers.ContentType?.MediaType);
        Assert.Equal("about:blank", response.String("type"));
        Assert.Equal(400, response.Json.GetProperty("status").GetInt32());
        Assert.Equal("validation.failed", response.Code);
        Assert.False(string.IsNullOrEmpty(response.String("title")));
        Assert.Equal("shape-check", response.String("correlationId"));
        Assert.Equal(System.Text.Json.JsonValueKind.Object, response.Json.GetProperty("errors").ValueKind);
        Assert.False(response.Json.TryGetProperty("detail", out _));
    }

    [Theory]
    [InlineData("/api/v1/auth/refresh")]
    [InlineData("/api/v1/auth/password-reset/complete")]
    public async Task BodiesOver16KiB_Are400ValidationFailed(string path)
    {
        var body = $"{{\"refreshToken\":\"{new string('a', 16 * 1024)}\"}}";
        var response = await Calls.SendAsync(_client, HttpMethod.Post, path, rawBody: body);
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
    }

    [Fact]
    public async Task ChangePassword_BodyOver16KiB_Is400ValidationFailed()
    {
        var user = await Seed.UserAsync(_database, _factory.Clock.UtcNow, ["Engineer"]);
        var token = (await Calls.LoginAsync(_client, user.Login, user.Password)).String("accessToken");
        var response = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/change-password",
            rawBody: $"{{\"currentPassword\":\"{new string('a', 16 * 1024)}\",\"newPassword\":\"x\"}}", bearer: token);
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
    }

    [Fact]
    public async Task Cors_ConfiguredOriginGetsCredentialedAccess_OthersGetNone()
    {
        async Task<HttpResponseMessage> PreflightAsync(string origin)
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/refresh");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "x-csrf-protection,content-type");
            return await _client.SendAsync(request);
        }

        var allowed = await PreflightAsync("https://admin.example.com");
        Assert.True(allowed.IsSuccessStatusCode);
        Assert.Equal("https://admin.example.com", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", allowed.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        Assert.Contains("x-csrf-protection", string.Join(",", allowed.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);

        var other = await PreflightAsync("https://evil.example.net");
        Assert.False(other.Headers.Contains("Access-Control-Allow-Origin"));

        using var simple = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = new StringContent("{}") };
        simple.Headers.Add("Origin", "https://evil.example.net");
        var simpleResponse = await _client.SendAsync(simple);
        Assert.False(simpleResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("not a url")]
    public void Cors_WildcardOrInvalidOrigin_StopsStartup(string origin)
    {
        using var factory = new ApiFactory(_database, new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = origin });
        Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());
    }

    [Fact]
    public async Task UnhandledFailure_Is500ServerError_WithNoInternals()
    {
        await using var factory = new ApiFactory(_database, configureServices: services =>
        {
            services.RemoveAll<ISecurityStampReader>();
            services.AddScoped<ISecurityStampReader, ExplodingStampReader>();
        });
        var client = factory.CreateApiClient();
        var user = await Seed.UserAsync(_database, factory.Clock.UtcNow, ["Engineer"]);
        var token = (await Calls.LoginAsync(client, user.Login, user.Password)).String("accessToken")!;

        var response = await Calls.MeAsync(client, token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.Status);
        Assert.Equal("server.error", response.Code);
        Assert.Equal("application/problem+json", response.Message.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(ExplodingStampReader.Message, response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", response.Body, StringComparison.Ordinal);
        Assert.True(Guid.TryParse(response.String("correlationId"), out _));
    }

    private sealed class ExplodingStampReader : ISecurityStampReader
    {
        public const string Message = "internal detail that must not leak";

        public Task<SecurityStamp?> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Message);
    }
}

/// <summary>Outside development: HTTPS only, no redirect, and HSTS (contract v1.5 §1, §18, E14).</summary>
public sealed class HttpsOnlyTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private readonly PostgresTestDatabaseFixture _database;
    private ApiFactory _staging = null!;

    public HttpsOnlyTests(PostgresTestDatabaseFixture database)
    {
        _database = database;
    }

    public Task InitializeAsync()
    {
        // Not Production: Production also fails closed on the audit privilege self-check,
        // and the test database connects as the table owner (ADR 0006 §7.3).
        _staging = new ApiFactory(_database, environment: "Staging");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _staging.DisposeAsync();

    [Fact]
    public async Task PlainHttp_IsNotServed_AndNotRedirected()
    {
        var client = _staging.CreateApiClient("http://api.example.com");
        var exception = await Record.ExceptionAsync(() => client.PostAsync("/api/v1/auth/login", new StringContent("{}")));
        Assert.NotNull(exception);
        Assert.True(_staging.Logs.Contains("Http.PlainHttpRefused"));
    }

    [Fact]
    public async Task Https_IsServed_WithStrictTransportSecurity()
    {
        var client = _staging.CreateApiClient("https://api.example.com");
        var response = await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}");
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("max-age=31536000", response.Header("Strict-Transport-Security"));
    }

    [Fact]
    public async Task TrustedProxyForwardedProto_IsHonoured_UntrustedIsNot()
    {
        await using var factory = new ApiFactory(_database, new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5" }, environment: "Staging");
        var client = factory.CreateApiClient("http://api.example.com");

        var viaProxy = await Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}",
            headers: new Dictionary<string, string> { [ApiFactory.TestRemoteIpHeader] = "10.0.0.5", ["X-Forwarded-Proto"] = "https", ["X-Forwarded-For"] = "203.0.113.9" });
        Assert.Equal(HttpStatusCode.BadRequest, viaProxy.Status);

        var spoofed = await Record.ExceptionAsync(() => Calls.SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}",
            headers: new Dictionary<string, string> { [ApiFactory.TestRemoteIpHeader] = "198.51.100.77", ["X-Forwarded-Proto"] = "https", ["X-Forwarded-For"] = "203.0.113.9" }));
        Assert.NotNull(spoofed);
    }

    [Fact]
    public async Task Development_ServesLocalHttp_WithoutHsts()
    {
        await using var development = new ApiFactory(_database);
        var response = await Calls.SendAsync(development.CreateApiClient(), HttpMethod.Post, "/api/v1/auth/login", rawBody: "{}");
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Null(response.Header("Strict-Transport-Security"));
    }
}
