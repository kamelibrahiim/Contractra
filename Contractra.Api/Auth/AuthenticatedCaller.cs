using Contractra.Domain.Identity;

namespace Contractra.Api.Auth;

/// <summary>
/// The caller of a request with a valid access token, after the per-request
/// security-version lookup (contract §3.2, §3.5). <see cref="MustChangePassword"/> is
/// read from the database on every request, never from a claim.
/// </summary>
public sealed record AuthenticatedCaller(
    Guid UserId,
    Guid SessionId,
    ClientApp App,
    int SecurityVersion,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Roles,
    bool MustChangePassword)
{
    public bool HasPermission(string code) => Permissions.Contains(code, StringComparer.Ordinal);
}

public static class AuthenticatedCallerExtensions
{
    public static AuthenticatedCaller? GetCaller(this HttpContext context) => context.Features.Get<AuthenticatedCaller>();

    public static AuthenticatedCaller RequireCaller(this HttpContext context) =>
        context.Features.Get<AuthenticatedCaller>() ?? throw new InvalidOperationException("The endpoint requires an authenticated caller.");
}
