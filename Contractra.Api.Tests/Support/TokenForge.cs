using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Contractra.Api.Tests.Support;

/// <summary>Builds access tokens with chosen headers and claims, to prove each validation rule of contract §3.2.</summary>
public static class TokenForge
{
    public static Dictionary<string, object> Claims(Guid userId, Guid sessionId, int securityVersion, string app = "employee") => new()
    {
        ["sub"] = userId.ToString("D"),
        ["sid"] = sessionId.ToString("D"),
        ["app"] = app,
        ["sv"] = securityVersion,
        ["perm"] = new[] { "Apps.Employee.Access" },
        ["role"] = new[] { "Engineer" },
    };

    public static string Sign(
        byte[] key,
        string? kid,
        IDictionary<string, object> claims,
        DateTimeOffset issuedAt,
        TimeSpan? lifetime = null,
        string issuer = "contractra-api",
        string audience = "contractra",
        string algorithm = SecurityAlgorithms.HmacSha256,
        DateTimeOffset? notBefore = null,
        bool includeNotBefore = true)
    {
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        var securityKey = new SymmetricSecurityKey(key) { KeyId = kid };
        var iat = DateTimeOffset.FromUnixTimeSeconds(issuedAt.ToUnixTimeSeconds());
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = iat.UtcDateTime,
            Expires = (iat + (lifetime ?? TimeSpan.FromMinutes(30))).UtcDateTime,
            SigningCredentials = new SigningCredentials(securityKey, algorithm),
            Claims = new Dictionary<string, object>(claims),
        };
        if (includeNotBefore)
        {
            descriptor.NotBefore = (notBefore ?? iat).UtcDateTime;
        }

        return handler.CreateToken(descriptor);
    }

    /// <summary>An unsigned token (<c>alg: none</c>).</summary>
    public static string Unsigned(IDictionary<string, object> claims, DateTimeOffset issuedAt, string kid)
    {
        var payload = new Dictionary<string, object>(claims)
        {
            ["iss"] = "contractra-api",
            ["aud"] = "contractra",
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["nbf"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = issuedAt.AddMinutes(30).ToUnixTimeSeconds(),
        };
        var header = new Dictionary<string, object> { ["alg"] = "none", ["typ"] = "JWT", ["kid"] = kid };
        return $"{Encode(header)}.{Encode(payload)}.";
    }

    /// <summary>Takes a real token and re-signs its payload with a different header alg but the same key (HS512).</summary>
    public static string Decode(string token)
    {
        var parts = token.Split('.');
        return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(parts[1]));
    }

    private static string Encode(object value) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
}
