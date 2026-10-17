using System.Text.Json;
using Contractra.Application.Identity.Auth;

namespace Contractra.Api.Http;

/// <summary>
/// RFC 9457 ProblemDetails with the stable <c>code</c> extension and the
/// <c>correlationId</c> (contract §12, §13). <c>detail</c> is never sent, so no secret or
/// internal can leak through it; <c>validation.failed</c> adds <c>errors</c>; a 429
/// carries <c>Retry-After</c> in whole seconds.
/// </summary>
public static class Problems
{
    public const string ContentType = "application/problem+json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(
        HttpContext context,
        int status,
        string code,
        string title,
        IReadOnlyDictionary<string, string[]>? errors = null,
        TimeSpan? retryAfter = null)
    {
        var response = context.Response;
        response.StatusCode = status;
        response.ContentType = ContentType;
        response.Headers.CacheControl = "no-store";
        if (retryAfter is { } wait)
        {
            response.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var body = new Dictionary<string, object?>
        {
            ["type"] = "about:blank",
            ["title"] = title,
            ["status"] = status,
            ["code"] = code,
            ["correlationId"] = CorrelationIdMiddleware.Get(context),
        };
        if (errors is not null)
        {
            body["errors"] = errors;
        }

        await JsonSerializer.SerializeAsync(response.Body, body, Json, context.RequestAborted);
    }

    public static int StatusFor(string code) => code switch
    {
        AuthErrorCodes.InvalidCredentials or AuthErrorCodes.Unauthenticated
            or AuthErrorCodes.RefreshInvalid or AuthErrorCodes.SessionExpired => StatusCodes.Status401Unauthorized,
        AuthErrorCodes.Forbidden or AuthErrorCodes.PasswordChangeRequired or AuthErrorCodes.AppAccessDenied => StatusCodes.Status403Forbidden,
        AuthErrorCodes.TooManyAttempts => StatusCodes.Status429TooManyRequests,
        AuthErrorCodes.ServerError => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status400BadRequest,
    };

    public static string TitleFor(string code) => code switch
    {
        AuthErrorCodes.ValidationFailed => "The request is invalid",
        AuthErrorCodes.InvalidCredentials => "Invalid credentials",
        AuthErrorCodes.Unauthenticated => "Authentication required",
        AuthErrorCodes.RefreshInvalid => "Invalid refresh token",
        AuthErrorCodes.SessionExpired => "Session expired",
        AuthErrorCodes.Forbidden => "Forbidden",
        AuthErrorCodes.PasswordChangeRequired => "Password change required",
        AuthErrorCodes.AppAccessDenied => "App access denied",
        AuthErrorCodes.CurrentPasswordIncorrect => "Current password incorrect",
        AuthErrorCodes.PasswordTooShort => "Password too short",
        AuthErrorCodes.PasswordTooLong => "Password too long",
        AuthErrorCodes.PasswordSameAsCurrent => "Password same as current",
        AuthErrorCodes.ResetCodeInvalid => "Reset code invalid",
        AuthErrorCodes.TooManyAttempts => "Too many attempts",
        _ => "Server error",
    };

    public static Task WriteCodeAsync(HttpContext context, string code, IReadOnlyDictionary<string, string[]>? errors = null, TimeSpan? retryAfter = null) =>
        WriteAsync(context, StatusFor(code), code, TitleFor(code), errors, retryAfter);

    public static Task ServerErrorAsync(HttpContext context) =>
        WriteAsync(context, StatusCodes.Status500InternalServerError, AuthErrorCodes.ServerError, TitleFor(AuthErrorCodes.ServerError));

    /// <summary>
    /// The §17 fail-closed response when the shared throttle store is unreachable: 503.
    /// PROVISIONAL (contract gap): §12 lists no code for this 503; <c>server.error</c>
    /// (listed for 500) is used so no code is invented. Pending confirmation.
    /// </summary>
    public static Task ServiceUnavailableAsync(HttpContext context) =>
        WriteAsync(context, StatusCodes.Status503ServiceUnavailable, AuthErrorCodes.ServerError, "Service unavailable");
}
