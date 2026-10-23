using System.Text;

namespace Contractra.Application.Identity.Credentials;

/// <summary>
/// Destination-number rules of plan §1F.5 / contract §23.2: normalise to E.164 and validate the format
/// only (spaces, dashes, dots and brackets removed; a leading <c>00</c> becomes <c>+</c>; a <c>+</c> is
/// required; 8-15 digits after it, the first not <c>0</c>). Format only: nothing here proves the number
/// belongs to the employee or is on WhatsApp.
/// </summary>
public static class WhatsAppNumber
{
    public static bool TryNormalize(string? input, out string e164)
    {
        e164 = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var builder = new StringBuilder();
        foreach (var c in input.Trim())
        {
            if (c is ' ' or '-' or '.' or '(' or ')')
            {
                continue;
            }

            builder.Append(c);
        }

        var text = builder.ToString();
        if (text.StartsWith("00", StringComparison.Ordinal))
        {
            text = "+" + text[2..];
        }

        if (text.Length < 2 || text[0] != '+')
        {
            return false;
        }

        var digits = text[1..];
        if (digits.Length is < 8 or > 15 || !digits.All(char.IsAsciiDigit) || digits[0] == '0')
        {
            return false;
        }

        e164 = text;
        return true;
    }

    /// <summary>Shows the country prefix and last two digits: <c>+20•••••••34</c>.</summary>
    public static string Mask(string e164)
    {
        if (e164.Length <= 6)
        {
            return new string('•', e164.Length);
        }

        return e164[..3] + new string('•', e164.Length - 5) + e164[^2..];
    }
}
