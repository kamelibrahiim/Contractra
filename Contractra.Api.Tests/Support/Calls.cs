using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Contractra.Api.Tests.Support;

public sealed record ApiResponse(HttpResponseMessage Message, string Body)
{
    public HttpStatusCode Status => Message.StatusCode;

    public JsonElement Json => JsonDocument.Parse(Body).RootElement;

    public string? Code => Body.Length > 0 && Json.TryGetProperty("code", out var code) ? code.GetString() : null;

    public string? Header(string name) =>
        Message.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
        : Message.Content.Headers.TryGetValues(name, out var contentValues) ? string.Join(",", contentValues) : null;

    public IReadOnlyList<string> SetCookies =>
        Message.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];

    public string? String(string property) => Json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public string AllText => $"{Message.Headers}\n{Message.Content.Headers}\n{Body}";
}

public static class Calls
{
    public static async Task<ApiResponse> SendAsync(
        HttpClient client, HttpMethod method, string url, object? body = null, string? bearer = null,
        IReadOnlyDictionary<string, string>? headers = null, string? rawBody = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (rawBody is not null)
        {
            request.Content = new StringContent(rawBody, Encoding.UTF8, "application/json");
        }
        else if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new ApiResponse(response, text);
    }

    public static Task<ApiResponse> LoginAsync(
        HttpClient client, string login, string password, string clientApp = "employee", string? platform = null, string? ip = null)
    {
        var headers = new Dictionary<string, string>();
        if (platform is not null)
        {
            headers["X-Client-Platform"] = platform;
        }

        if (ip is not null)
        {
            headers[ApiFactory.TestRemoteIpHeader] = ip;
        }

        return SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", new { loginIdentifier = login, password, clientApp }, headers: headers);
    }

    public static Task<ApiResponse> RefreshMobileAsync(HttpClient client, string refreshToken, string? ip = null) =>
        SendAsync(client, HttpMethod.Post, "/api/v1/auth/refresh", new { refreshToken },
            headers: ip is null ? null : new Dictionary<string, string> { [ApiFactory.TestRemoteIpHeader] = ip });

    public static Task<ApiResponse> RefreshWebAsync(HttpClient client, string cookieToken, bool csrfHeader = true)
    {
        var headers = new Dictionary<string, string> { ["Cookie"] = $"contractra_refresh={cookieToken}" };
        if (csrfHeader)
        {
            headers["X-CSRF-Protection"] = "1";
        }

        return SendAsync(client, HttpMethod.Post, "/api/v1/auth/refresh", headers: headers);
    }

    public static Task<ApiResponse> MeAsync(HttpClient client, string accessToken) =>
        SendAsync(client, HttpMethod.Get, "/api/v1/auth/me", bearer: accessToken);

    public static Task<ApiResponse> ChangePasswordAsync(HttpClient client, string accessToken, string currentPassword, string newPassword) =>
        SendAsync(client, HttpMethod.Post, "/api/v1/auth/change-password", new { currentPassword, newPassword }, bearer: accessToken);

    public static Task<ApiResponse> LogoutAsync(HttpClient client, string accessToken, bool csrfHeader = false)
    {
        var headers = new Dictionary<string, string>();
        if (csrfHeader)
        {
            headers["X-CSRF-Protection"] = "1";
        }

        return SendAsync(client, HttpMethod.Post, "/api/v1/auth/logout", bearer: accessToken, headers: headers);
    }

    public static Task<ApiResponse> CompleteResetAsync(HttpClient client, string login, string resetCode, string newPassword, string? ip = null) =>
        SendAsync(client, HttpMethod.Post, "/api/v1/auth/password-reset/complete", new { loginIdentifier = login, resetCode, newPassword },
            headers: ip is null ? null : new Dictionary<string, string> { [ApiFactory.TestRemoteIpHeader] = ip });

    /// <summary>The value of the contractra_refresh cookie set by a response, or null.</summary>
    public static string? RefreshCookieValue(ApiResponse response)
    {
        var cookie = response.SetCookies.FirstOrDefault(c => c.StartsWith("contractra_refresh=", StringComparison.Ordinal));
        if (cookie is null)
        {
            return null;
        }

        var value = cookie["contractra_refresh=".Length..].Split(';')[0];
        return value;
    }
}
