using Contractra.Domain.Identity;
using FrameworkHasher = Microsoft.AspNetCore.Identity.PasswordHasher<Contractra.Domain.Identity.User>;
using FrameworkResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace Contractra.Infrastructure.Identity;

/// <summary>
/// Wraps <see cref="FrameworkHasher"/> (auth contract v1.5 R33). The
/// <see cref="User"/> passed to the framework hasher is never inspected by
/// it; only the password string is hashed.
/// </summary>
public sealed class AspNetPasswordHasher : Contractra.Application.Identity.IPasswordHasher
{
    private readonly FrameworkHasher _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(user: null!, password);

    public Contractra.Application.Identity.PasswordVerificationResult Verify(string hash, string password)
    {
        var result = _hasher.VerifyHashedPassword(user: null!, hash, password);
        return result switch
        {
            FrameworkResult.Success => Contractra.Application.Identity.PasswordVerificationResult.Success,
            FrameworkResult.SuccessRehashNeeded => Contractra.Application.Identity.PasswordVerificationResult.SuccessRehashNeeded,
            _ => Contractra.Application.Identity.PasswordVerificationResult.Failed,
        };
    }
}
