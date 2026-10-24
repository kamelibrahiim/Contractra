namespace Contractra.Api.Auth;

/// <summary>
/// The web refresh cookie of contract v1.5 §3.4 (owner decisions R6, R22):
/// <c>contractra_refresh=&lt;token&gt;; HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth;
/// Expires=&lt;session absolute expiry&gt;</c>, host-only (no Domain), never extended by
/// rotation. Logout or revocation answers with the same cookie, empty, Max-Age=0.
/// </summary>
public static class RefreshCookie
{
    public const string Name = "contractra_refresh";
    public const string Path = "/api/v1/auth";

    public static void Set(HttpResponse response, string token, DateTimeOffset sessionAbsoluteExpiresAt) =>
        response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path,
            Expires = sessionAbsoluteExpiresAt,
            IsEssential = true,
        });

    public static void Clear(HttpResponse response) =>
        response.Cookies.Append(Name, string.Empty, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path,
            MaxAge = TimeSpan.Zero,
            IsEssential = true,
        });

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value) ? value : null;
}
