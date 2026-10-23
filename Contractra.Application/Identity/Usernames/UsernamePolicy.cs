using System.Text;

namespace Contractra.Application.Identity.Usernames;

/// <summary>Why a candidate username was refused (username policy v1 §1); the wire value is the lower_snake form.</summary>
public enum UsernameInvalidReason
{
    Whitespace,
    TooShort,
    TooLong,
    BadCharacters,
    BadShape,
    PhoneLike,
    Reserved,
}

public sealed record UsernameValidation(string? Canonical, UsernameInvalidReason? Reason)
{
    public bool IsValid => Reason is null;

    public static string ReasonCode(UsernameInvalidReason reason) => reason switch
    {
        UsernameInvalidReason.Whitespace => "whitespace",
        UsernameInvalidReason.TooShort => "too_short",
        UsernameInvalidReason.TooLong => "too_long",
        UsernameInvalidReason.BadCharacters => "bad_characters",
        UsernameInvalidReason.BadShape => "bad_shape",
        UsernameInvalidReason.PhoneLike => "phone_like",
        _ => "reserved",
    };
}

/// <summary>
/// Username policy v1 (docs/implementation/backend/features/username-policy-v1.md §1, §4):
/// a creation-time validator for new accounts. <c>LoginIdentifier</c> stays opaque (ADR 0003 §3.1);
/// existing and bootstrap identifiers are never re-validated. Pure, no I/O.
/// </summary>
public sealed class UsernamePolicy
{
    public const string Version = "v1";
    public const int MinLength = 3;
    public const int MaxLength = 32;
    private const int PhoneLikeDigitRun = 6;

    private static readonly string[] DefaultReserved =
    [
        "admin", "administrator", "root", "system", "support", "contractra", "owner", "accountant",
        "engineer", "officestaff", "office", "staff", "service", "bot", "noreply", "api", "null",
        "undefined", "me",
    ];

    private static readonly string[] PrivilegedWords = ["admin", "administrator", "root", "system", "support"];

    private readonly HashSet<string> _reserved;

    /// <param name="extraReserved">Role codes and the configured bootstrap identifier (compared case-insensitively).</param>
    public UsernamePolicy(IEnumerable<string>? extraReserved = null)
    {
        _reserved = new HashSet<string>(DefaultReserved, StringComparer.OrdinalIgnoreCase);
        if (extraReserved is not null)
        {
            foreach (var name in extraReserved.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                _reserved.Add(name.Trim());
            }
        }
    }

    public UsernameValidation Validate(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return Invalid(UsernameInvalidReason.TooShort);
        }

        if (candidate.Any(char.IsWhiteSpace))
        {
            return Invalid(UsernameInvalidReason.Whitespace);
        }

        if (candidate.Length < MinLength)
        {
            return Invalid(UsernameInvalidReason.TooShort);
        }

        if (candidate.Length > MaxLength)
        {
            return Invalid(UsernameInvalidReason.TooLong);
        }

        var canonical = AsciiLower(candidate);
        if (canonical.Any(c => !IsAllowed(c)))
        {
            return Invalid(UsernameInvalidReason.BadCharacters);
        }

        if (!IsLetter(canonical[0]) || !char.IsAsciiLetterOrDigit(canonical[^1]) || HasDoubleSeparator(canonical))
        {
            return Invalid(UsernameInvalidReason.BadShape);
        }

        if (HasDigitRun(canonical, PhoneLikeDigitRun))
        {
            return Invalid(UsernameInvalidReason.PhoneLike);
        }

        if (IsReserved(canonical))
        {
            return Invalid(UsernameInvalidReason.Reserved);
        }

        return new UsernameValidation(canonical, null);
    }

    private bool IsReserved(string canonical)
    {
        if (_reserved.Contains(canonical))
        {
            return true;
        }

        foreach (var word in PrivilegedWords)
        {
            if (canonical.StartsWith(word, StringComparison.Ordinal)
                && canonical.Length > word.Length
                && canonical.Skip(word.Length).All(char.IsAsciiDigit))
            {
                return true;
            }
        }

        return false;
    }

    private static UsernameValidation Invalid(UsernameInvalidReason reason) => new(null, reason);

    private static string AsciiLower(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c is >= 'A' and <= 'Z' ? (char)(c + 32) : c);
        }

        return builder.ToString();
    }

    private static bool IsLetter(char c) => c is >= 'a' and <= 'z';

    private static bool IsSeparator(char c) => c is '.' or '_' or '-';

    private static bool IsAllowed(char c) => IsLetter(c) || char.IsAsciiDigit(c) || IsSeparator(c);

    private static bool HasDoubleSeparator(string value)
    {
        for (var i = 1; i < value.Length; i++)
        {
            if (IsSeparator(value[i]) && IsSeparator(value[i - 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDigitRun(string value, int length)
    {
        var run = 0;
        foreach (var c in value)
        {
            run = char.IsAsciiDigit(c) ? run + 1 : 0;
            if (run >= length)
            {
                return true;
            }
        }

        return false;
    }
}
