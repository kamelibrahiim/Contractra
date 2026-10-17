using System.Net;
using Contractra.Api.Tests.Support;
using Contractra.Application.Identity.Operations;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.IntegrationTests;

namespace Contractra.Api.Tests;

/// <summary>POST /api/v1/auth/password-reset/complete - contract v1.5 §5.6, §10A, §14.</summary>
public sealed class PasswordResetCompletionEndpointTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private readonly PostgresTestDatabaseFixture _database;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public PasswordResetCompletionEndpointTests(PostgresTestDatabaseFixture database)
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

    /// <summary>Issues a reset through the real operator use case (§10A) and returns the code shown once.</summary>
    private async Task<string> IssueAsync(SeededUser user)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<OperatorIssuePasswordResetUseCase>()
            .ExecuteAsync(new OperatorIssuePasswordResetRequest(user.Login, "test reset", Guid.NewGuid().ToString("D")));
        Assert.True(result.IsSuccess, result.Error?.ToString());
        return result.Value!.Code;
    }

    [Fact]
    public async Task Success_Is204_SetsThePassword_CreatesNoSession_AndAudits()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var code = await IssueAsync(user);
        var sessionsBefore = (await Seed.SessionsOfAsync(_database, user.Id)).Count;

        var response = await Calls.CompleteResetAsync(_client, user.Login.ToUpperInvariant(), code.ToLowerInvariant().Replace("-", " "), "Chosen-Passw0rd");

        Assert.Equal(HttpStatusCode.NoContent, response.Status);
        Assert.Equal(sessionsBefore, (await Seed.SessionsOfAsync(_database, user.Id)).Count);
        var audit = Assert.Single(await Seed.AuditEventsAsync(_database, user.Id), e => e.ActionType == AuditActionType.PasswordResetCompleted);
        Assert.Equal(AuditActorType.User, audit.ActorType);
        Assert.Equal(user.Id, audit.EntityId);
        Assert.NotNull(audit.Snapshot);
        Assert.Equal(HttpStatusCode.OK, (await Calls.LoginAsync(_client, user.Login, "Chosen-Passw0rd")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.LoginAsync(_client, user.Login, user.Password)).Status);
    }

    [Fact]
    public async Task Success_RevokesEverySession()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var code = await IssueAsync(user);
        Assert.Equal(HttpStatusCode.NoContent, (await Calls.CompleteResetAsync(_client, user.Login, code, "Chosen-Passw0rd")).Status);
        var session = await Calls.LoginAsync(_client, user.Login, "Chosen-Passw0rd");
        var secondCode = await IssueAsync(user);

        Assert.Equal(HttpStatusCode.NoContent, (await Calls.CompleteResetAsync(_client, user.Login, secondCode, "Chosen-Again-1")).Status);

        Assert.All(await Seed.SessionsOfAsync(_database, user.Id), s => Assert.NotNull(s.RevokedAt));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Calls.RefreshMobileAsync(_client, session.String("refreshToken")!)).Status);
    }

    [Fact]
    public async Task EveryInvalidCodeCase_IsTheSameGeneric400()
    {
        var now = Now;
        var user = await Seed.UserAsync(_database, now, ["Engineer"]);
        var code = await IssueAsync(user);
        var disabled = await Seed.UserAsync(_database, now, ["Engineer"], disabled: true);

        var wrong = await Calls.CompleteResetAsync(_client, user.Login, "AAAA-BBBB-CCCC-DDDD", "Chosen-Passw0rd");
        var unknown = await Calls.CompleteResetAsync(_client, Seed.NewLogin("nobody"), code, "Chosen-Passw0rd");
        var disabledUser = await Calls.CompleteResetAsync(_client, disabled.Login, code, "Chosen-Passw0rd");

        foreach (var response in new[] { wrong, unknown, disabledUser })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.Status);
            Assert.Equal("auth.reset_code_invalid", response.Code);
        }

        // A failed attempt never burns the ticket.
        Assert.Equal(HttpStatusCode.NoContent, (await Calls.CompleteResetAsync(_client, user.Login, code, "Chosen-Passw0rd")).Status);

        // Consumed.
        var reused = await Calls.CompleteResetAsync(_client, user.Login, code, "Chosen-Again-1");
        Assert.Equal("auth.reset_code_invalid", reused.Code);
    }

    [Fact]
    public async Task ExpiredOrSupersededCode_IsTheSameGeneric400()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var first = await IssueAsync(user);
        var second = await IssueAsync(user);

        var superseded = await Calls.CompleteResetAsync(_client, user.Login, first, "Chosen-Passw0rd");
        Assert.Equal("auth.reset_code_invalid", superseded.Code);

        _factory.Clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        var expired = await Calls.CompleteResetAsync(_client, user.Login, second, "Chosen-Passw0rd");
        Assert.Equal(HttpStatusCode.BadRequest, expired.Status);
        Assert.Equal("auth.reset_code_invalid", expired.Code);
    }

    [Theory]
    [InlineData("short", "auth.password_too_short")]
    [InlineData("LONG", "auth.password_too_long")]
    public async Task NewPasswordRules_Are400WithTheirCodes(string newPassword, string code)
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var reset = await IssueAsync(user);
        var response = await Calls.CompleteResetAsync(_client, user.Login, reset, newPassword == "LONG" ? new string('x', 129) : newPassword);
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal(code, response.Code);
    }

    [Theory]
    [InlineData("{\"resetCode\":\"AAAA\",\"newPassword\":\"Chosen-Passw0rd\"}", "loginIdentifier")]
    [InlineData("{\"loginIdentifier\":\"x\",\"newPassword\":\"Chosen-Passw0rd\"}", "resetCode")]
    [InlineData("{\"loginIdentifier\":\"x\",\"resetCode\":\"AAAA\"}", "newPassword")]
    [InlineData("{\"loginIdentifier\":\"x\",\"resetCode\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"newPassword\":\"Chosen-Passw0rd\"}", "resetCode")]
    public async Task ShapeAndLimits_Are400ValidationFailed(string body, string field)
    {
        var response = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/password-reset/complete", rawBody: body);
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
        Assert.True(response.Json.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task OverLimitIdentifier_Is400ValidationFailed()
    {
        var response = await Calls.CompleteResetAsync(_client, new string('i', 257), "AAAA-BBBB-CCCC-DDDD", "Chosen-Passw0rd");
        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("validation.failed", response.Code);
    }

    [Fact]
    public async Task NoCsrfHeaderIsNeeded_AndNoCookieIsRead()
    {
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var code = await IssueAsync(user);
        var response = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/auth/password-reset/complete",
            new { loginIdentifier = user.Login, resetCode = code, newPassword = "Chosen-Passw0rd" },
            headers: new Dictionary<string, string> { ["Cookie"] = "contractra_refresh=whatever" });
        Assert.Equal(HttpStatusCode.NoContent, response.Status);
    }

    [Fact]
    public async Task ProvisionalAuditSource_IsRecordedAsTheSingleNamedConstant()
    {
        // Contract gap G1 (Slice 6 report): no app is identifiable on this endpoint.
        var user = await Seed.UserAsync(_database, Now, ["Engineer"]);
        var code = await IssueAsync(user);
        Assert.Equal(HttpStatusCode.NoContent, (await Calls.CompleteResetAsync(_client, user.Login, code, "Chosen-Passw0rd")).Status);
        var audit = Assert.Single(await Seed.AuditEventsAsync(_database, user.Id), e => e.ActionType == AuditActionType.PasswordResetCompleted);
        Assert.Equal(Contractra.Api.Auth.AuthEndpoints.PasswordResetCompletionAuditSource, audit.Source);
    }
}
