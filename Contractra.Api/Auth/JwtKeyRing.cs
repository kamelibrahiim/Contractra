using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;

namespace Contractra.Api.Auth;

/// <summary>
/// The signing key ring of contract §3.2.1, read once at startup from the secret store:
/// <c>Auth:Jwt:ActiveKeyId</c> names the one active key (signs new tokens);
/// <c>Auth:Jwt:SigningKeys:&lt;kid&gt;</c> holds each key as base64 (the active key and
/// any verification-only keys). Planned rotation: add the new key, make it active,
/// keep the previous one for at least 31 minutes, then remove it. Emergency rotation:
/// replace the ring with a new active key only. A missing ring, a missing active key or
/// a key shorter than 256 bits stops startup (fail closed); a key is never generated.
/// Key values are never logged or included in an error message.
/// </summary>
public sealed partial class JwtKeyRing
{
    public const int MinKeyBytes = 32;
    public const string Section = "Auth:Jwt";

    private readonly IReadOnlyDictionary<string, SymmetricSecurityKey> _keys;

    private JwtKeyRing(string issuer, string audience, string activeKeyId, IReadOnlyDictionary<string, SymmetricSecurityKey> keys)
    {
        Issuer = issuer;
        Audience = audience;
        ActiveKeyId = activeKeyId;
        _keys = keys;
    }

    public string Issuer { get; }

    public string Audience { get; }

    public string ActiveKeyId { get; }

    public SymmetricSecurityKey ActiveKey => _keys[ActiveKeyId];

    public IReadOnlyCollection<string> KeyIds => _keys.Keys.ToList();

    /// <summary>The key named by <paramref name="kid"/>, only if it is in the ring - never "try every key".</summary>
    public IEnumerable<SecurityKey> Resolve(string? kid) =>
        kid is not null && _keys.TryGetValue(kid, out var key) ? [key] : [];

    public static JwtKeyRing Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        var issuer = section["Issuer"] is { Length: > 0 } i ? i : "contractra-api";
        var audience = section["Audience"] is { Length: > 0 } a ? a : "contractra";
        var activeKeyId = section["ActiveKeyId"];
        if (string.IsNullOrWhiteSpace(activeKeyId))
        {
            throw new InvalidOperationException("Auth:Jwt:ActiveKeyId is not configured; startup refuses to run without a signing key (contract §3.2.1).");
        }

        var keys = new Dictionary<string, SymmetricSecurityKey>(StringComparer.Ordinal);
        foreach (var entry in section.GetSection("SigningKeys").GetChildren())
        {
            if (!KeyIdPattern().IsMatch(entry.Key))
            {
                throw new InvalidOperationException("A signing key id must be 1-64 characters from A-Z a-z 0-9 - _ . (contract §3.2.1).");
            }

            byte[] material;
            try
            {
                material = Convert.FromBase64String(entry.Value ?? string.Empty);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"Signing key '{entry.Key}' is not valid base64.");
            }

            if (material.Length < MinKeyBytes)
            {
                throw new InvalidOperationException($"Signing key '{entry.Key}' is shorter than 256 bits (contract §3.2.1).");
            }

            keys[entry.Key] = new SymmetricSecurityKey(material) { KeyId = entry.Key };
        }

        if (!keys.ContainsKey(activeKeyId))
        {
            throw new InvalidOperationException($"The active signing key '{activeKeyId}' is not in Auth:Jwt:SigningKeys (contract §3.2.1).");
        }

        return new JwtKeyRing(issuer, audience, activeKeyId, keys);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex KeyIdPattern();
}
