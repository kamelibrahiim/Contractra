using System.Net;
using System.Text;
using Contractra.Api.Integrations;
using Contractra.Api.Tests.Support;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.Identity;
using Contractra.Infrastructure.IntegrationTests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contractra.Api.Tests;

/// <summary>
/// The WhatsApp webhook (contract v1.6 §23.6): the verify-token handshake, the raw-body HMAC signature checked before
/// anything is parsed, idempotent and monotonic status updates, and 200 for anything well signed. Payloads follow Meta's
/// documented webhook shape as known when the tests were written; PS-04b re-verifies them against Meta's current documentation.
/// </summary>
public sealed class WhatsAppWebhookEndpointTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private const string Number = "+201000000001";

    private readonly PostgresTestDatabaseFixture _database;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public WhatsAppWebhookEndpointTests(PostgresTestDatabaseFixture database)
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

    private static string StatusPayload(string messageId, string status, long timestamp, string? errorCode = null)
    {
        var statusEntry = new Dictionary<string, object?>
        {
            ["id"] = messageId,
            ["status"] = status,
            ["timestamp"] = timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["recipient_id"] = "201000000001",
        };
        if (errorCode is not null)
        {
            statusEntry["errors"] = new[] { new Dictionary<string, object?> { ["code"] = int.Parse(errorCode), ["title"] = "Message undeliverable" } };
        }

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            @object = "whatsapp_business_account",
            entry = new[]
            {
                new
                {
                    id = "1",
                    changes = new[]
                    {
                        new { field = "messages", value = new { messaging_product = "whatsapp", statuses = new[] { statusEntry } } },
                    },
                },
            },
        });
    }

    private async Task<HttpResponseMessage> PostAsync(string body, string? signature = null, bool sign = true)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, WhatsAppWebhookEndpoints.Route)
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation(WhatsAppWebhookEndpoints.SignatureHeader, signature);
        }
        else if (sign)
        {
            request.Headers.TryAddWithoutValidation(
                WhatsAppWebhookEndpoints.SignatureHeader, WhatsAppWebhookEndpoints.Sign(bytes, ApiFactory.WebhookAppSecret));
        }

        return await _client.SendAsync(request);
    }

    /// <summary>An account created through the API whose message the worker has sent (so it carries a provider message id).</summary>
    private async Task<(Guid IssuanceId, string MessageId)> AcceptedIssuanceAsync()
    {
        var admin = await Seed.UserAsync(_database, _factory.Clock.UtcNow, ["Admin"]);
        var token = (await Calls.LoginAsync(_client, admin.Login, admin.Password, "admin")).String("accessToken")!;
        var created = await Calls.SendAsync(_client, HttpMethod.Post, "/api/v1/users", new
        {
            loginIdentifier = Seed.NewPolicyLogin("hook"),
            roleCodes = new[] { "OfficeStaff" },
            whatsapp = new { number = Number, consent = new { affirmed = true, basis = "verbal" } },
        }, token);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var issuanceId = Guid.Parse(created.Json.GetProperty("credentialDelivery").GetProperty("issuanceId").GetString()!);

        await _factory.Services.GetRequiredService<CredentialDeliveryWorker>().RunOnceAsync(CancellationToken.None);

        await using var db = _database.CreateDbContext();
        var issuance = await db.CredentialIssuances.AsNoTracking().SingleAsync(i => i.Id == issuanceId);
        Assert.Equal(CredentialDeliveryStatus.Accepted, issuance.Status);
        return (issuanceId, issuance.ProviderMessageId!);
    }

    private async Task<CredentialIssuance> ReloadAsync(Guid id)
    {
        await using var db = _database.CreateDbContext();
        return await db.CredentialIssuances.AsNoTracking().SingleAsync(i => i.Id == id);
    }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ---- handshake ---------------------------------------------------------------------

    [Fact]
    public async Task Handshake_EchoesTheChallenge_OnlyForTheConfiguredVerifyToken()
    {
        var ok = await _client.GetAsync($"{WhatsAppWebhookEndpoints.Route}?hub.mode=subscribe&hub.verify_token={ApiFactory.WebhookVerifyToken}&hub.challenge=12345");
        var wrongToken = await _client.GetAsync($"{WhatsAppWebhookEndpoints.Route}?hub.mode=subscribe&hub.verify_token=nope&hub.challenge=12345");
        var wrongMode = await _client.GetAsync($"{WhatsAppWebhookEndpoints.Route}?hub.mode=unsubscribe&hub.verify_token={ApiFactory.WebhookVerifyToken}&hub.challenge=12345");
        var noToken = await _client.GetAsync($"{WhatsAppWebhookEndpoints.Route}?hub.mode=subscribe&hub.challenge=12345");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("12345", await ok.Content.ReadAsStringAsync());
        Assert.All([wrongToken, wrongMode, noToken], r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    [Fact]
    public async Task WithNoAppSecretOrVerifyTokenConfigured_TheEndpointDoesNotExist()
    {
        await using var bare = new ApiFactory(_database, new Dictionary<string, string?> { ["WhatsApp:AppSecret"] = "", ["WhatsApp:WebhookVerifyToken"] = "" });
        var client = bare.CreateApiClient();

        var post = await client.PostAsync(WhatsAppWebhookEndpoints.Route, new StringContent("{}", Encoding.UTF8, "application/json"));
        var get = await client.GetAsync($"{WhatsAppWebhookEndpoints.Route}?hub.mode=subscribe&hub.verify_token=&hub.challenge=1");

        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
    }

    // ---- signature ---------------------------------------------------------------------

    [Fact]
    public async Task APostWithoutAValidSignature_Is401_AndChangesNothing()
    {
        var (issuanceId, messageId) = await AcceptedIssuanceAsync();
        var body = StatusPayload(messageId, "delivered", Now);
        var bytes = Encoding.UTF8.GetBytes(body);

        var unsigned = await PostAsync(body, sign: false);
        var wrongSecret = await PostAsync(body, WhatsAppWebhookEndpoints.Sign(bytes, "another-secret"));
        var garbage = await PostAsync(body, "sha256=zz");
        var noPrefix = await PostAsync(body, Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiFactory.WebhookAppSecret), bytes)));
        // Signed over a different body: a modified payload must not verify.
        var tampered = await PostAsync(body, WhatsAppWebhookEndpoints.Sign(Encoding.UTF8.GetBytes(body + " "), ApiFactory.WebhookAppSecret));

        Assert.All([unsigned, wrongSecret, garbage, noPrefix, tampered], r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Equal(CredentialDeliveryStatus.Accepted, (await ReloadAsync(issuanceId)).Status);
    }

    [Fact]
    public async Task ABodyOverTheLimit_Is413_BeforeAnythingIsParsed()
    {
        var huge = new string(' ', WhatsAppWebhookEndpoints.MaxBodyBytes + 10);

        var response = await PostAsync(huge);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // ---- processing ---------------------------------------------------------------------

    [Fact]
    public async Task ADeliveredEvent_EndsTheIssuance_AndPurgesTheSealedCopy()
    {
        var (issuanceId, messageId) = await AcceptedIssuanceAsync();

        var response = await PostAsync(StatusPayload(messageId, "delivered", Now));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var issuance = await ReloadAsync(issuanceId);
        Assert.Equal(CredentialDeliveryStatus.Delivered, issuance.Status);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task AReplayedEvent_IsAcknowledged_AndAppliedOnce()
    {
        var (issuanceId, messageId) = await AcceptedIssuanceAsync();
        var timestamp = Now;
        var body = StatusPayload(messageId, "sent", timestamp);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(body)).StatusCode);

        Assert.Equal(CredentialDeliveryStatus.Sent, (await ReloadAsync(issuanceId)).Status);
        await using var db = _database.CreateDbContext();
        Assert.Equal(1, await db.WhatsAppWebhookEvents.CountAsync(e => e.ProviderMessageId == messageId && e.Status == "sent"));
    }

    [Fact]
    public async Task AStatusThatArrivesLate_NeverMovesTheIssuanceBackwards()
    {
        var (issuanceId, messageId) = await AcceptedIssuanceAsync();
        await PostAsync(StatusPayload(messageId, "read", Now));

        var late = await PostAsync(StatusPayload(messageId, "sent", Now - 60));

        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        Assert.Equal(CredentialDeliveryStatus.Read, (await ReloadAsync(issuanceId)).Status);
    }

    [Fact]
    public async Task AFailedEvent_FailsTheIssuance_WithAMappedCategory_AndStoresOnlyTheCodeAndTitle()
    {
        var (issuanceId, messageId) = await AcceptedIssuanceAsync();

        var response = await PostAsync(StatusPayload(messageId, "failed", Now, errorCode: "131026"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var issuance = await ReloadAsync(issuanceId);
        Assert.Equal(CredentialDeliveryStatus.Failed, issuance.Status);
        Assert.Equal(CredentialFailureCategory.NotOnWhatsApp, issuance.FailureCategory);
        Assert.False(issuance.HasSealedCopy);

        await using var connection = new Npgsql.NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT t::text FROM whatsapp_webhook_events t WHERE provider_message_id = @id;", connection);
        command.Parameters.AddWithValue("id", messageId);
        var row = (string)(await command.ExecuteScalarAsync())!;
        Assert.DoesNotContain("201000000001", row); // the recipient is never stored
    }

    [Fact]
    public async Task AnUnknownMessageId_AMalformedSignedBody_AndAnUnknownStatus_AreAllAcknowledged()
    {
        var unknown = await PostAsync(StatusPayload("wamid.does-not-exist", "delivered", Now));
        var malformed = await PostAsync("this is not json");
        var wrongShape = await PostAsync("{\"entry\":5}");
        var (issuanceId, messageId) = await AcceptedIssuanceAsync();
        var odd = await PostAsync(StatusPayload(messageId, "deleted", Now));

        Assert.All([unknown, malformed, wrongShape, odd], r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(CredentialDeliveryStatus.Accepted, (await ReloadAsync(issuanceId)).Status);
    }

    [Fact]
    public async Task TheEndpoint_NeedsNoAccessToken_AndLogsNothingOfThePayload()
    {
        var (_, messageId) = await AcceptedIssuanceAsync();

        var response = await PostAsync(StatusPayload(messageId, "delivered", Now));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("201000000001", _factory.Logs.AllText);
        Assert.DoesNotContain(ApiFactory.WebhookAppSecret, _factory.Logs.AllText);
    }

    [Fact]
    public void TheSignatureCheck_AcceptsOnlyTheExactHmac()
    {
        var body = "{\"a\":1}"u8.ToArray();
        var good = WhatsAppWebhookEndpoints.Sign(body, "secret");

        Assert.True(WhatsAppWebhookEndpoints.SignatureValid(good, body, "secret"));
        Assert.True(WhatsAppWebhookEndpoints.SignatureValid(good.ToUpperInvariant().Replace("SHA256=", "sha256="), body, "secret"));
        Assert.False(WhatsAppWebhookEndpoints.SignatureValid(good, body, "other"));
        Assert.False(WhatsAppWebhookEndpoints.SignatureValid(good, "{\"a\":2}"u8, "secret"));
        Assert.False(WhatsAppWebhookEndpoints.SignatureValid(string.Empty, body, "secret"));
        Assert.False(WhatsAppWebhookEndpoints.SignatureValid("sha256=", body, "secret"));
    }
}
