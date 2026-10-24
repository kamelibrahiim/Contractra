using Contractra.Api.Http;
using Contractra.Application.Identity.Auth;
using Contractra.Domain.Audit;

namespace Contractra.Api.Auth;

/// <summary>
/// Every 403 to an authenticated user is an AccessDenied audit event and a Serilog
/// entry (contract §6, §14); the audit row is written first, in its own scope
/// (§14.2), and a failed write never turns the denial into an allow. The web
/// CSRF-header rejection (§8) is the one 403 that is Serilog only (<see cref="CsrfRejectedAsync"/>).
/// </summary>
public static class AccessDenial
{
    public static async Task DenyAsync(
        HttpContext context, string code, Guid actorUserId, AuditSource source, string? requiredPermission)
    {
        var action = $"{context.Request.Method} {context.Request.Path}";
        var correlationId = CorrelationIdMiddleware.Get(context);
        Logger(context).LogWarning(
            "Auth.Forbidden Code={Code} UserId={UserId} Action={Action} CorrelationId={CorrelationId}",
            code, actorUserId, action, correlationId);

        await context.RequestServices.GetRequiredService<IAccessDeniedAuditor>().RecordAsync(
            new AccessDeniedRecord(actorUserId, source, requiredPermission, code, action, correlationId));

        await Problems.WriteCodeAsync(context, code);
    }

    /// <summary>Contract §8 layer 2: web refresh/logout without <c>X-CSRF-Protection: 1</c>. Serilog only, never AccessDenied (R40).</summary>
    public static Task CsrfRejectedAsync(HttpContext context)
    {
        Logger(context).LogWarning(
            "Auth.CsrfRejected Action={Action} CorrelationId={CorrelationId}: the X-CSRF-Protection header is missing.",
            $"{context.Request.Method} {context.Request.Path}", CorrelationIdMiddleware.Get(context));
        return Problems.WriteCodeAsync(context, AuthErrorCodes.Forbidden);
    }

    public const string CsrfHeaderName = "X-CSRF-Protection";

    public static bool HasCsrfHeader(HttpRequest request)
    {
        var values = request.Headers[CsrfHeaderName];
        return values.Count == 1 && values[0] == "1";
    }

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Contractra.Api.Auth.AccessDenial");
}

/// <summary>
/// Runs after authentication (contract §4.1 order): token validation and the
/// security-version lookup have already happened (<see cref="JwtValidation"/>); this
/// applies the password-change gate (§7), then the endpoint's app audience, then the
/// permission it declares (§9 steps 2-4). An
/// endpoint that declares no audience is refused (fail closed, §4.1).
/// <c>X-Client-Platform</c> is never consulted here.
/// </summary>
public sealed class AudienceGateMiddleware
{
    /// <summary>The display name ASP.NET Core routing gives its built-in 405 endpoint. If it ever changes, the 405 endpoint is simply refused (fail closed).</summary>
    public const string MethodNotAllowedEndpointName = "405 HTTP Method Not Supported";

    private readonly RequestDelegate _next;

    public AudienceGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null || endpoint.DisplayName == MethodNotAllowedEndpointName)
        {
            // No endpoint (404), or routing's own 405 endpoint, which runs nothing but the 405.
            await _next(context);
            return;
        }

        var audience = endpoint.Metadata.GetMetadata<AuthAudienceMetadata>()?.Audience;
        if (audience == AuthAudience.Public)
        {
            await _next(context);
            return;
        }

        var caller = context.GetCaller();
        if (caller is null)
        {
            await Problems.WriteCodeAsync(context, AuthErrorCodes.Unauthenticated);
            return;
        }

        var source = AppAccess.AuditSourceFor(caller.App);
        var endpointPermission = endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()?.Permission;

        if (audience is null)
        {
            await AccessDenial.DenyAsync(context, AuthErrorCodes.Forbidden, caller.UserId, source, endpointPermission);
            return;
        }

        if (caller.MustChangePassword && endpoint.Metadata.GetMetadata<AllowedWhilePasswordChangeRequiredMetadata>() is null)
        {
            await AccessDenial.DenyAsync(context, AuthErrorCodes.PasswordChangeRequired, caller.UserId, source, endpointPermission);
            return;
        }

        var required = audience switch
        {
            AuthAudience.Admin => (App: (Contractra.Domain.Identity.ClientApp?)Contractra.Domain.Identity.ClientApp.Admin, Permission: AppAccess.AdminPermission),
            AuthAudience.Employee => (App: Contractra.Domain.Identity.ClientApp.Employee, Permission: AppAccess.EmployeePermission),
            _ => (App: null, Permission: null),
        };

        if (required.App is { } app && (caller.App != app || !caller.HasPermission(required.Permission!)))
        {
            await AccessDenial.DenyAsync(context, AuthErrorCodes.AppAccessDenied, caller.UserId, source, required.Permission);
            return;
        }

        // Contract §9 step 4: the endpoint's own permission (Users.Manage on every users endpoint).
        if (endpointPermission is not null && !caller.HasPermission(endpointPermission))
        {
            await AccessDenial.DenyAsync(context, AuthErrorCodes.Forbidden, caller.UserId, source, endpointPermission);
            return;
        }

        await _next(context);
    }
}
