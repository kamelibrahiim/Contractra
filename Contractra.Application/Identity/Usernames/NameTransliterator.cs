using System.Globalization;
using System.Text;

namespace Contractra.Application.Identity.Usernames;

/// <summary>
/// Name normalisation and Arabic transliteration of username policy v1 (§2, §3). The dictionary
/// and letter map are an engineering seed that the owner must review (OD-19). Pure, no I/O.
/// </summary>
public static class NameTransliterator
{
    private static readonly Dictionary<string, string> Dictionary = new()
    {
        ["محمد"] = "mohamed", ["احمد"] = "ahmed", ["محمود"] = "mahmoud", ["مصطفى"] = "mostafa",
        ["ابراهيم"] = "ibrahim", ["اسماعيل"] = "ismail", ["علي"] = "ali", ["حسن"] = "hassan",
        ["حسين"] = "hussein", ["خالد"] = "khaled", ["عمر"] = "omar", ["عثمان"] = "osman",
        ["يوسف"] = "youssef", ["كريم"] = "karim", ["طارق"] = "tarek", ["سعيد"] = "said",
        ["سيد"] = "sayed", ["كامل"] = "kamel", ["هاني"] = "hany", ["ياسر"] = "yasser",
        ["عادل"] = "adel", ["ماجد"] = "maged", ["سامي"] = "samy", ["رمضان"] = "ramadan",
        ["صلاح"] = "salah", ["جمال"] = "gamal", ["فتحي"] = "fathy", ["عبد الله"] = "abdullah",
        ["عبد الرحمن"] = "abdelrahman", ["عبد العزيز"] = "abdelaziz", ["عبد الحميد"] = "abdelhamid",
        ["عبد الرحيم"] = "abdelrahim",
    };

    private static readonly Dictionary<char, string> LetterMap = new()
    {
        ['ا'] = "a", ['أ'] = "a", ['إ'] = "a", ['آ'] = "a", ['ٱ'] = "a", ['ب'] = "b", ['ت'] = "t",
        ['ث'] = "th", ['ج'] = "j", ['ح'] = "h", ['خ'] = "kh", ['د'] = "d", ['ذ'] = "dh", ['ر'] = "r",
        ['ز'] = "z", ['س'] = "s", ['ش'] = "sh", ['ص'] = "s", ['ض'] = "d", ['ط'] = "t", ['ظ'] = "z",
        ['ع'] = "a", ['غ'] = "gh", ['ف'] = "f", ['ق'] = "q", ['ك'] = "k", ['ل'] = "l", ['م'] = "m",
        ['ن'] = "n", ['ه'] = "h", ['ة'] = "a", ['ى'] = "a", ['ء'] = "", ['ئ'] = "", ['ؤ'] = "",
    };

    private static readonly HashSet<string> ArabicConnectors = ["عبد", "ابو", "بن", "ابن"];

    private static readonly HashSet<string> LatinConnectors =
        new(["abd", "abdel", "abdul", "abu", "bin", "ibn", "al", "el"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The usable tokens of <paramref name="name"/> (Latin letters and digits only), in order.</summary>
    public static IReadOnlyList<string> Tokens(string? name)
    {
        var cleaned = Clean(name);
        if (cleaned.Length == 0)
        {
            return [];
        }

        var raw = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var merged = new List<string>();
        for (var i = 0; i < raw.Length; i++)
        {
            var token = raw[i];
            var isConnector = ArabicConnectors.Contains(NormalizeAlef(token)) || LatinConnectors.Contains(token);
            if (isConnector && i + 1 < raw.Length)
            {
                token = token + " " + raw[++i];
            }

            merged.Add(token);
        }

        var result = new List<string>();
        foreach (var token in merged)
        {
            var latin = IsArabic(token) ? TransliterateArabic(token) : ToLatin(token);
            if (latin.Length > 0)
            {
                result.Add(latin);
            }
        }

        return result;
    }

    private static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormKC))
        {
            if (c is >= '​' and <= '‏' or >= '‪' and <= '‮' or >= '⁦' and <= '⁩' or '﻿')
            {
                continue;
            }

            if (c is >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ')
            {
                continue;
            }

            builder.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsArabic(string token) => token.Any(c => c is >= '؀' and <= 'ۿ');

    private static string NormalizeAlef(string text) =>
        text.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ٱ', 'ا');

    private static string ToLatin(string token)
    {
        var decomposed = token.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(lower);
            }
        }

        return builder.ToString();
    }

    private static string TransliterateArabic(string token)
    {
        var key = NormalizeAlef(token);
        if (Dictionary.TryGetValue(key, out var known))
        {
            return known;
        }

        var parts = key.Split(' ', 2);
        if (parts.Length == 2 && parts[0] == "عبد")
        {
            var rest = parts[1];
            var prefix = "abd";
            if (rest.StartsWith("ال", StringComparison.Ordinal) && rest.Length > 2)
            {
                prefix = "abdel";
                rest = rest[2..];
            }

            return prefix + MapLetters(rest);
        }

        return MapLetters(key.Replace(" ", string.Empty));
    }

    private static string MapLetters(string text)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == 'و')
            {
                builder.Append(i == 0 ? "w" : "u");
            }
            else if (c == 'ي')
            {
                builder.Append(i == 0 ? "y" : "i");
            }
            else if (LetterMap.TryGetValue(c, out var mapped))
            {
                builder.Append(mapped);
            }
            else if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
