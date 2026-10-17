using System.Globalization;
using Contractra.Api.Http;
using Contractra.Application.Identity.Auth;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Api.Auth;

/// <summary>
/// <c>/api/v1/auth/*</c> (auth contract v1.5 §5). Each endpoint declares its §4.1
/// audience; the public ones carry the §17 layer 1 (and refresh layer 5) rate limits;
/// the four §7 endpoints are allowed while a password change is required. Handlers only
/// translate HTTP to the Application use cases and back.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// PROVISIONAL - contract gap (Slice 6 report, G1). CompletePasswordResetUseCase needs
    /// the audit <c>source</c> (contract §14.1: Admin app → Web, Employee app → EmployeeApp),
    /// but <c>POST /auth/password-reset/complete</c> is one public endpoint for both apps and
    /// its request (§5.6) identifies no app; <c>X-Client-Platform</c> is read at login only
    /// (§5.1) and is not an app. Until the owner decides, every PasswordResetCompleted event
    /// is written with this value.
    /// </summary>
    public const AuditSource PasswordResetCompletionAuditSource = AuditSource.Web;

    public const string ClientPlatformHeader = "X-Client-Platform";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth");

        auth.MapPost("/login", LoginAsync)
            .WithAudience(AuthAudience.Public)
            .WithRateLimitLayers(publicLayer: true, refreshLayer: false);

        auth.MapPost("/refresh", RefreshAsync)
            .WithAudience(AuthAudience.Public)
            .WithRateLimitLayers(publicLayer: true, refreshLayer: true);

        auth.MapPost("/password-reset/complete", CompletePasswordResetAsync)
            .WithAudience(AuthAudience.Public)
            .WithRateLimitLayers(publicLayer: true, refreshLayer: false);

        auth.MapPost("/logout", LogoutAsync)
            .WithAudience(AuthAudience.AnySession)
            .AllowedWhilePasswordChangeRequired();

        auth.MapGet("/me", MeAsync)
            .WithAudience(AuthAudience.AnySession)
            .AllowedWhilePasswordChangeRequired();

        auth.MapPost("/change-password", ChangePasswordAsync)
            .WithAudience(AuthAudience.AnySession)
            .AllowedWhilePasswordChangeRequired();

        return app;
    }

    // POST /api/v1/auth/login - §5.1
    private static async Task LoginAsync(HttpContext context)
    {
        var body = await AuthRequestBody.ReadAsync(context.Request);
        if (body.Status == BodyStatus.TooLarge)
        {
            // Over-limit input on login is the generic 401, with no field-level hint (§1).
            Logger(context).LogInformation("Auth.LoginFailed Reason={Reason}", "input_over_limit");
            await Problems.WriteCodeAsync(context, AuthErrorCodes.InvalidCredentials);
            return;
        }

        if (body.Status == BodyStatus.Malformed)
        {
            await MalformedAsync(context);
            return;
        }

        var platformHeader = context.Request.Headers.TryGetValue(ClientPlatformHeader, out var platform) ? platform.ToString() : null;
        var request = new LoginRequest(
            body.GetString("loginIdentifier"),
            body.GetString("password"),
            body.GetString("clientApp"),
            platformHeader,
            ClientSource.Of(context),
            body.TypeErrorMessages());

        var outcome = await context.RequestServices.GetRequiredService<LoginUseCase>().ExecuteAsync(request, context.RequestAborted);
        if (!outcome.IsSuccess)
        {
            await WriteFailureAsync(context, outcome.Failure!);
            return;
        }

        await WriteTokensAsync(context, outcome.Value!);
    }

    // POST /api/v1/auth/refresh - §5.2, §8
    private static async Task RefreshAsync(HttpContext context)
    {
        var body = await AuthRequestBody.ReadAsync(context.Request);
        if (body.Status is BodyStatus.TooLarge or BodyStatus.Malformed)
        {
            await MalformedAsync(context);
            return;
        }

        var token = body.GetString("refreshToken");
        if (body.TypeErrors.Count > 0)
        {
            await Problems.WriteCodeAsync(context, AuthErrorCodes.ValidationFailed, body.TypeErrorMessages());
            return;
        }

        var viaCookie = false;
        if (string.IsNullOrEmpty(token) && RefreshCookie.Read(context.Request) is { } cookieToken)
        {
            // Web: the cookie is an ambient credential, so the custom header is required (§8).
            if (!AccessDenial.HasCsrfHeader(context.Request))
            {
                await AccessDenial.CsrfRejectedAsync(context);
                return;
            }

            token = cookieToken;
            viaCookie = true;
        }

        var outcome = await WithConcurrencyRetryAsync(context, services =>
            services.GetRequiredService<RefreshUseCase>().ExecuteAsync(token, context.RequestAborted));

        if (!outcome.IsSuccess)
        {
            if (viaCookie && outcome.Failure!.Code is AuthErrorCodes.RefreshInvalid or AuthErrorCodes.SessionExpired)
            {
                RefreshCookie.Clear(context.Response);
            }

            await WriteFailureAsync(context, outcome.Failure!);
            return;
        }

        await WriteTokensAsync(context, outcome.Value!);
    }

    // POST /api/v1/auth/logout - §5.3, §8
    private static async Task LogoutAsync(HttpContext context)
    {
        var caller = context.RequireCaller();
        var result = await context.RequestServices.GetRequiredService<LogoutUseCase>()
            .ExecuteAsync(caller.UserId, caller.SessionId, AccessDenial.HasCsrfHeader(context.Request), context.RequestAborted);

        if (result.Status == LogoutStatus.CsrfHeaderMissing)
        {
            await AccessDenial.CsrfRejectedAsync(context);
            return;
        }

        if (result.Platform == ClientPlatform.Web || RefreshCookie.Read(context.Request) is not null)
        {
            RefreshCookie.Clear(context.Response);
        }

        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    // GET /api/v1/auth/me - §5.4
    private static async Task MeAsync(HttpContext context)
    {
        var caller = context.RequireCaller();
        var user = await context.RequestServices.GetRequiredService<CurrentUserQuery>().ExecuteAsync(caller.UserId, context.RequestAborted);
        if (user is null)
        {
            await Problems.WriteCodeAsync(context, AuthErrorCodes.Unauthenticated);
            return;
        }

        await context.Response.WriteAsJsonAsync(new
        {
            id = user.Id,
            loginIdentifier = user.LoginIdentifier,
            roles = user.Roles,
            permissions = user.Permissions,
            mustChangePassword = user.MustChangePassword,
        });
    }

    // POST /api/v1/auth/change-password - §5.5
    private static async Task ChangePasswordAsync(HttpContext context)
    {
        var caller = context.RequireCaller();
        var body = await AuthRequestBody.ReadAsync(context.Request);
        if (body.Status is BodyStatus.TooLarge or BodyStatus.Malformed)
        {
            await MalformedAsync(context);
            return;
        }

        var attempt = new ChangePasswordAttempt(
            caller.UserId,
            caller.SessionId,
            body.GetString("currentPassword"),
            body.GetString("newPassword"),
            CorrelationIdMiddleware.Get(context),
            AppAccess.AuditSourceFor(caller.App),
            body.TypeErrorMessages());

        var changed = await context.RequestServices.GetRequiredService<ThrottledChangePassword>().ExecuteAsync(attempt, context.RequestAborted);
        if (!changed.IsSuccess)
        {
            await WriteFailureAsync(context, changed.Failure!);
            return;
        }

        // The password change has committed (ChangePasswordUseCase). Rotating the current
        // session's refresh token is a separate transaction in a fresh scope (option (b) of
        // ChangePasswordUseCase's note): if it fails the client gets 500, keeps its still
        // unconsumed refresh token, and recovers through the normal refresh after a 401.
        AuthOutcome<SessionTokens> rotated;
        try
        {
            await using var scope = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            rotated = await WithConcurrencyRetryAsync(context, services =>
                services.GetRequiredService<RefreshUseCase>().RotateAfterPasswordChangeAsync(caller.UserId, caller.SessionId, context.RequestAborted),
                scope.ServiceProvider);
        }
        catch (Exception exception)
        {
            Logger(context).LogError(exception, "Auth.ChangePasswordRotationFailed UserId={UserId} SessionId={SessionId}: the password was changed.", caller.UserId, caller.SessionId);
            await Problems.ServerErrorAsync(context);
            return;
        }

        if (!rotated.IsSuccess)
        {
            Logger(context).LogError("Auth.ChangePasswordRotationFailed UserId={UserId} SessionId={SessionId}: the password was changed.", caller.UserId, caller.SessionId);
            await Problems.ServerErrorAsync(context);
            return;
        }

        await WriteTokensAsync(context, rotated.Value!);
    }

    // POST /api/v1/auth/password-reset/complete - §5.6
    private static async Task CompletePasswordResetAsync(HttpContext context)
    {
        var body = await AuthRequestBody.ReadAsync(context.Request);
        if (body.Status is BodyStatus.TooLarge or BodyStatus.Malformed)
        {
            await MalformedAsync(context);
            return;
        }

        var attempt = new PasswordResetCompletionAttempt(
            body.GetString("loginIdentifier"),
            body.GetString("resetCode"),
            body.GetString("newPassword"),
            ClientSource.Of(context),
            CorrelationIdMiddleware.Get(context),
            PasswordResetCompletionAuditSource,
            body.TypeErrorMessages());

        AuthOutcome<bool> outcome;
        try
        {
            outcome = await context.RequestServices.GetRequiredService<ThrottledPasswordResetCompletion>().ExecuteAsync(attempt, context.RequestAborted);
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            // A concurrent completion (or a new issue) won the ticket: same generic answer as every other failure (§5.6).
            Logger(context).LogInformation("Auth.ResetCompletionFailed Reason={Reason}", "concurrent_completion");
            await Problems.WriteCodeAsync(context, AuthErrorCodes.ResetCodeInvalid);
            return;
        }

        if (!outcome.IsSuccess)
        {
            await WriteFailureAsync(context, outcome.Failure!);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static async Task WriteTokensAsync(HttpContext context, SessionTokens tokens)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";

        string? bodyRefreshToken = null;
        if (tokens.Platform == ClientPlatform.Web)
        {
            if (tokens.RefreshToken is not null)
            {
                RefreshCookie.Set(context.Response, tokens.RefreshToken, tokens.SessionAbsoluteExpiresAt);
            }
        }
        else
        {
            bodyRefreshToken = tokens.RefreshToken;
        }

        var body = new Dictionary<string, object?>
        {
            ["accessToken"] = tokens.AccessToken.Token,
            ["accessTokenExpiresAt"] = tokens.AccessToken.ExpiresAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };
        if (bodyRefreshToken is not null)
        {
            body["refreshToken"] = bodyRefreshToken;
        }

        body["mustChangePassword"] = tokens.MustChangePassword;
        await context.Response.WriteAsJsonAsync(body);
    }

    private static Task WriteFailureAsync(HttpContext context, AuthFailure failure) => failure.Kind switch
    {
        AuthFailureKind.Throttled => Problems.WriteCodeAsync(context, AuthErrorCodes.TooManyAttempts, retryAfter: failure.RetryAfter),
        AuthFailureKind.ThrottleUnavailable => Problems.ServiceUnavailableAsync(context),
        _ when failure.DeniedActor is { } actor =>
            AccessDenial.DenyAsync(context, failure.Code, actor.UserId, actor.Source, actor.RequiredPermission),
        _ => Problems.WriteCodeAsync(context, failure.Code, failure.FieldErrors),
    };

    private static Task MalformedAsync(HttpContext context) =>
        Problems.WriteCodeAsync(context, AuthErrorCodes.ValidationFailed, new Dictionary<string, string[]>
        {
            ["body"] = [$"The body must be a JSON object of at most {AuthRequestBody.MaxBytes} bytes."],
        });

    /// <summary>Runs once; on a lost concurrent write (ADR 0005) runs once more in a fresh scope.</summary>
    private static async Task<T> WithConcurrencyRetryAsync<T>(HttpContext context, Func<IServiceProvider, Task<T>> run, IServiceProvider? services = null)
    {
        try
        {
            return await run(services ?? context.RequestServices);
        }
        catch (Exception exception) when (IsConcurrencyConflict(exception))
        {
            await using var retryScope = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            return await run(retryScope.ServiceProvider);
        }
    }

    private static bool IsConcurrencyConflict(Exception exception) =>
        exception is DbUpdateConcurrencyException
        || exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation or PostgresErrorCodes.SerializationFailure };

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Contractra.Api.Auth.AuthEndpoints");
}
