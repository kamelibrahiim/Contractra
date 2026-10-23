using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Contractra.Api.Auth;
using Contractra.Application.Identity.Credentials;

namespace Contractra.Api.Integrations;

/// <summary>
/// The WhatsApp webhook (contract v1.6 §23.6). A machine endpoint outside the admin/employee audiences:
/// its protection is the verify token (handshake), the HMAC-SHA-256 signature of the <b>raw</b> body
/// (checked in constant time before anything is parsed), a fixed body-size limit and idempotent,
/// monotonic processing. Only message id, status, timestamp and error code/title are stored; the payload
/// and the recipient are never logged or stored.
/// <para>
/// Header and field names (<c>X-Hub-Signature-256: sha256=&lt;hex&gt;</c>; <c>hub.mode</c>, <c>hub.verify_token</c>,
/// <c>hub.challenge</c>; <c>entry[].changes[].value.statuses[]</c> with <c>id</c>, <c>status</c>, <c>timestamp</c>,
/// <c>errors[]</c>) follow Meta's WhatsApp Cloud API webhook documentation as known when this was written. PS-04b
/// must re-verify them against Meta's current documentation before go-live (the date and source are recorded then).
/// </para>
/// </summary>
public static class WhatsAppWebhookEndpoints
{
    public const string Route = "/api/v1/integrations/whatsapp/webhook";
    public const string SignatureHeader = "X-Hub-Signature-256";
    public const int MaxBodyBytes = 256 * 1024;
    public const string VerifyTokenKey = "WhatsApp:WebhookVerifyToken";
    public const string AppSecretKey = "WhatsApp:AppSecret";

    public static IEndpointRouteBuilder MapWhatsAppWebhook(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route).WithAudience(AuthAudience.Public);
        group.MapGet("", HandshakeAsync);
        group.MapPost("", ReceiveAsync);
        return app;
    }

    private static async Task HandshakeAsync(HttpContext context)
    {
        var configured = context.RequestServices.GetRequiredService<IConfiguration>()[VerifyTokenKey];
        var query = context.Request.Query;
        if (string.IsNullOrEmpty(configured)
            || query["hub.mode"] != "subscribe"
            || !FixedTimeEquals(query["hub.verify_token"].ToString(), configured))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(query["hub.challenge"].ToString());
    }

    private static async Task ReceiveAsync(HttpContext context)
    {
        var secret = context.RequestServices.GetRequiredService<IConfiguration>()[AppSecretKey];
        if (string.IsNullOrEmpty(secret))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound; // not configured: the endpoint does not exist
            return;
        }

        if (context.Request.ContentLength is > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var buffer = new byte[MaxBodyBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length
            && (read = await context.Request.Body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), context.RequestAborted)) > 0)
        {
            total += read;
        }

        if (total > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var raw = buffer.AsSpan(0, total);
        if (!SignatureValid(context.Request.Headers[SignatureHeader].ToString(), raw, secret))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var events = ParseStatuses(raw);
        var apply = context.RequestServices.GetRequiredService<ApplyWhatsAppStatusUseCase>();
        foreach (var statusEvent in events)
        {
            await apply.ExecuteAsync(
                statusEvent.MessageId, statusEvent.Status, statusEvent.Timestamp, statusEvent.ErrorCode, statusEvent.ErrorTitle,
                context.RequestAborted);
        }

        // Any well-signed event is acknowledged, including unknown ids, so Meta does not retry endlessly.
        context.Response.StatusCode = StatusCodes.Status200OK;
    }

    /// <summary>Constant-time check of <c>sha256=&lt;hex&gt;</c> against the HMAC-SHA-256 of the raw body.</summary>
    public static bool SignatureValid(string header, ReadOnlySpan<byte> body, string appSecret)
    {
        const string Prefix = "sha256=";
        if (!header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(header[Prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body);
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    public static string Sign(ReadOnlySpan<byte> body, string appSecret) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body)).ToLowerInvariant();

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private sealed record StatusEvent(string MessageId, string Status, DateTimeOffset Timestamp, string? ErrorCode, string? ErrorTitle);

    private static List<StatusEvent> ParseStatuses(ReadOnlySpan<byte> raw)
    {
        var result = new List<StatusEvent>();
        try
        {
            using var document = JsonDocument.Parse(raw.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("entry", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value)
                        || !value.TryGetProperty("statuses", out var statuses)
                        || statuses.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var status in statuses.EnumerateArray())
                    {
                        var id = status.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
                        var name = status.TryGetProperty("status", out var statusProperty) ? statusProperty.GetString() : null;
                        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || id.Length > 256)
                        {
                            continue;
                        }

                        var timestamp = DateTimeOffset.UtcNow;
                        if (status.TryGetProperty("timestamp", out var tsProperty)
                            && long.TryParse(tsProperty.ValueKind == JsonValueKind.String ? tsProperty.GetString() : tsProperty.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                        {
                            timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds);
                        }

                        string? errorCode = null;
                        string? errorTitle = null;
                        if (status.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                        {
                            var first = errors[0];
                            errorCode = first.TryGetProperty("code", out var code) ? code.ToString() : null;
                            errorTitle = first.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() : null;
                        }

                        result.Add(new StatusEvent(id, name, timestamp, errorCode, errorTitle));
                    }
                }
            }
        }
        catch (JsonException)
        {
            // A signed but malformed body carries nothing we can use; acknowledge it.
        }
        catch (ArgumentOutOfRangeException)
        {
            // An out-of-range timestamp: ignore the body rather than fail the delivery.
        }

        return result;
    }
}
