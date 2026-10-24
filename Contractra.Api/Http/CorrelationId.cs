using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Serilog.Context;

namespace Contractra.Api.Http;

/// <summary>
/// Contract v1.5 §13: <c>X-Correlation-ID</c> is optional; a client value is accepted
/// only if it is 1-64 characters from <c>A-Z a-z 0-9 - _ .</c>, otherwise it is ignored
/// (never an error) and a UUID is generated. The ID is returned on every response,
/// written to Serilog and to every audit event the request creates. A replaced client
/// value is logged by length and hash only, never raw (proposed contract §22 I16).
/// </summary>
public sealed partial class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private const string ItemKey = "Contractra.CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    public static string Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) && value is string id ? id : throw new InvalidOperationException("No correlation id was assigned to this request.");

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName];
        var candidate = supplied.Count == 1 ? supplied[0] : null;
        string correlationId;
        if (IsValid(candidate))
        {
            correlationId = candidate!;
        }
        else
        {
            correlationId = Guid.NewGuid().ToString("D");
            if (supplied.Count > 0)
            {
                var raw = string.Join(",", supplied.ToArray());
                _logger.LogInformation(
                    "Http.CorrelationIdReplaced SuppliedLength={Length} SuppliedSha256={Hash} CorrelationId={CorrelationId}",
                    raw.Length, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant(), correlationId);
            }
        }

        context.Items[ItemKey] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex Pattern();
}
