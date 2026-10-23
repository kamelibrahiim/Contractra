namespace Contractra.Application.Identity.Usernames;

/// <summary>One suggestion; <see cref="Source"/> is <c>latinName</c> or <c>name</c>.</summary>
public sealed record UsernameSuggestion(string Value, string Source, bool Available);

public sealed record UsernameSuggestions(
    string PolicyVersion,
    IReadOnlyList<UsernameSuggestion> Suggestions,
    IReadOnlyList<string> Alternatives,
    string? Reason);

/// <summary>
/// Suggests usernames from a name (username policy v1 §2). <c>isTaken</c> reports whether a
/// canonical value is already used by any account (disabled included); availability is advisory,
/// the unique index decides at creation.
/// </summary>
public sealed class UsernameSuggester
{
    private const int MaxAlternatives = 3;
    private const int MaxSuffixSearch = 200;

    private readonly UsernamePolicy _policy;

    public UsernameSuggester(UsernamePolicy policy)
    {
        _policy = policy;
    }

    public async Task<UsernameSuggestions> SuggestAsync(
        string? name, string? latinName, Func<string, Task<bool>> isTaken)
    {
        var chosen = BaseFrom(latinName, "latinName") ?? BaseFrom(name, "name");
        if (chosen is null)
        {
            return new UsernameSuggestions(UsernamePolicy.Version, [], [], "name_unusable");
        }

        var (baseName, source) = chosen.Value;
        var free = new List<string>();
        for (var suffix = 1; suffix <= MaxSuffixSearch && free.Count < 1 + MaxAlternatives; suffix++)
        {
            var candidate = WithSuffix(baseName, suffix);
            if (_policy.Validate(candidate) is not { IsValid: true, Canonical: { } canonical })
            {
                if (suffix == 1)
                {
                    // The unsuffixed base itself is unusable (too short, reserved, ...): no suggestion.
                    return new UsernameSuggestions(UsernamePolicy.Version, [], [], "name_unusable");
                }

                continue;
            }

            if (!await isTaken(canonical))
            {
                free.Add(canonical);
            }
        }

        if (free.Count == 0)
        {
            return new UsernameSuggestions(UsernamePolicy.Version, [], [], "name_unusable");
        }

        return new UsernameSuggestions(
            UsernamePolicy.Version,
            [new UsernameSuggestion(free[0], source, true)],
            free.Skip(1).ToList(),
            null);
    }

    /// <summary>Up to three free values next to a taken, valid Admin-typed one.</summary>
    public async Task<IReadOnlyList<string>> AlternativesAsync(string canonicalBase, Func<string, Task<bool>> isTaken)
    {
        var result = new List<string>();
        for (var suffix = 2; suffix <= MaxSuffixSearch && result.Count < MaxAlternatives; suffix++)
        {
            var candidate = WithSuffix(canonicalBase, suffix);
            if (_policy.Validate(candidate) is { IsValid: true, Canonical: { } canonical } && !await isTaken(canonical))
            {
                result.Add(canonical);
            }
        }

        return result;
    }

    private static (string Base, string Source)? BaseFrom(string? text, string source)
    {
        var tokens = NameTransliterator.Tokens(text);
        if (tokens.Count == 0)
        {
            return null;
        }

        var joined = tokens.Count == 1 ? tokens[0] : tokens[0] + "." + tokens[1];
        if (joined.Length < 2 || joined.All(char.IsAsciiDigit))
        {
            return null;
        }

        return (joined, source);
    }

    private static string WithSuffix(string baseName, int suffix)
    {
        var tail = suffix == 1 ? string.Empty : suffix.ToString();
        var room = UsernamePolicy.MaxLength - tail.Length;
        var head = baseName.Length > room ? baseName[..room].TrimEnd('.', '_', '-') : baseName;
        return head + tail;
    }
}
