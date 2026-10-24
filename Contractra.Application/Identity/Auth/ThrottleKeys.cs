using System.Security.Cryptography;
using System.Text;

namespace Contractra.Application.Identity.Auth;

/// <summary>Throttle keys of contract §17.</summary>
public static class ThrottleKeys
{
    /// <summary>
    /// The identifier key: SHA-256 of the lower-cased submitted login identifier,
    /// computed whether or not the account exists, so throttling reveals nothing about
    /// existence and raw identifiers are never stored (contract §17).
    /// </summary>
    public static byte[] IdentifierKey(string submittedLoginIdentifier)
    {
        ArgumentNullException.ThrowIfNull(submittedLoginIdentifier);
        return SHA256.HashData(Encoding.UTF8.GetBytes(submittedLoginIdentifier.ToLowerInvariant()));
    }

    /// <summary>Lower-case hex, for Serilog (the key, never the raw identifier - contract §17).</summary>
    public static string ToHex(byte[] key) => Convert.ToHexString(key).ToLowerInvariant();
}
