using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Contractra.Application.Identity.Auth;

namespace Contractra.Infrastructure.Identity;

/// <summary>
/// Opaque refresh tokens (auth contract v1.5 §3.1): 32 bytes from a cryptographically
/// secure generator, base64url. Only the SHA-256 of the token's text is stored.
/// </summary>
public sealed class SecureRefreshTokenFactory : IRefreshTokenFactory
{
    public const int TokenBytes = 32;

    public NewRefreshToken Create()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        return new NewRefreshToken(token, Hash(token));
    }

    public byte[] Hash(string presentedToken)
    {
        ArgumentNullException.ThrowIfNull(presentedToken);
        return SHA256.HashData(Encoding.UTF8.GetBytes(presentedToken));
    }
}
