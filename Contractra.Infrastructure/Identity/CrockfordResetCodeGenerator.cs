using System.Security.Cryptography;
using System.Text;
using Contractra.Application.Identity;

namespace Contractra.Infrastructure.Identity;

/// <summary>
/// Auth contract v1.5 §10A, owner decision R44, engineering decision E1: 16
/// characters from the Crockford base-32 alphabet (80 bits), shown in four
/// groups of four. Hyphens, spaces and case are ignored on input.
/// </summary>
public sealed class CrockfordResetCodeGenerator : IResetCodeGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Length = 16;

    public GeneratedResetCode Generate()
    {
        var code = RandomNumberGenerator.GetString(Alphabet, Length);
        return new GeneratedResetCode(FormatForDisplay(code), HashNormalized(code));
    }

    public byte[] Hash(string submittedCode)
    {
        ArgumentNullException.ThrowIfNull(submittedCode);
        return HashNormalized(Normalize(submittedCode));
    }

    private static string FormatForDisplay(string code) =>
        string.Join('-', Enumerable.Range(0, Length / 4).Select(i => code.Substring(i * 4, 4)));

    private static string Normalize(string code)
    {
        var builder = new StringBuilder(code.Length);
        foreach (var c in code)
        {
            if (c is '-' or ' ')
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    private static byte[] HashNormalized(string normalizedCode) =>
        SHA256.HashData(Encoding.ASCII.GetBytes(normalizedCode));
}
