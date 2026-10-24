using System.Security.Cryptography;
using Contractra.Application.Identity;

namespace Contractra.Infrastructure.Identity;

/// <summary>
/// Auth contract v1.5 §10, owner decision R2, engineering decision E4: 12
/// characters from A-Z, a-z, 0-9, with at least one of each class, by
/// rejection sampling so the accepted distribution stays uniform.
/// </summary>
public sealed class SecureTemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const int Length = 12;

    public string Generate()
    {
        while (true)
        {
            var candidate = RandomNumberGenerator.GetString(Alphabet, Length);

            if (candidate.Any(char.IsUpper) && candidate.Any(char.IsLower) && candidate.Any(char.IsDigit))
            {
                return candidate;
            }
        }
    }
}
