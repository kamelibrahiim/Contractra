using Contractra.Application.Identity.Usernames;

namespace Contractra.Application.Tests.Identity.Usernames;

/// <summary>The golden vectors of docs/implementation/backend/features/username-policy-v1.md §5.</summary>
public class UsernamePolicyTests
{
    private static readonly UsernameSuggester Suggester = new(new UsernamePolicy(["Engineer", "Accountant", "Admin.Boot"]));

    private static Task<bool> Nobody(string _) => Task.FromResult(false);

    private static Func<string, Task<bool>> Taken(params string[] existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        return value => Task.FromResult(set.Contains(value));
    }

    [Theory]
    [InlineData("Ahmed Mohamed", null, "ahmed.mohamed")] // G1
    [InlineData("  AHMED   MOHAMED ", null, "ahmed.mohamed")] // G2
    [InlineData("أحمد محمد", null, "ahmed.mohamed")] // G3
    [InlineData("عبد الله حسن", null, "abdullah.hassan")] // G4
    [InlineData("José Núñez", null, "jose.nunez")] // G5
    [InlineData("محمد", null, "mohamed")] // G6
    [InlineData("O'Brien Patrick", null, "obrien.patrick")] // G8
    [InlineData("أحمد مُحَمَّد", null, "ahmed.mohamed")] // G9 diacritics
    [InlineData("أحمـد محمد", null, "ahmed.mohamed")] // G10 tatweel
    [InlineData("عبد الرحمن علي", null, "abdelrahman.ali")] // G11
    [InlineData("سلمى", null, "slma")] // G12 letter map (no short vowels)
    [InlineData("ندى أحمد", null, "nda.ahmed")] // G13
    [InlineData("أحمد محمد", "Ahmed Mohammed", "ahmed.mohammed")] // G14 latinName wins
    public async Task Suggest_FollowsTheGoldenVectors(string name, string? latinName, string expected)
    {
        var result = await Suggester.SuggestAsync(name, latinName, Nobody);

        Assert.Null(result.Reason);
        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(expected, suggestion.Value);
        Assert.Equal("v1", result.PolicyVersion);
    }

    [Theory]
    [InlineData("???")] // G7
    [InlineData("A")] // G15
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234")]
    public async Task Suggest_GivesNothingForAnUnusableName(string name)
    {
        var result = await Suggester.SuggestAsync(name, null, Nobody);

        Assert.Empty(result.Suggestions);
        Assert.Equal("name_unusable", result.Reason);
    }

    [Fact]
    public async Task Suggest_UsesTheNextFreeSuffixInOrder() // G16
    {
        var result = await Suggester.SuggestAsync("Ahmed Mohamed", null, Taken("ahmed.mohamed", "ahmed.mohamed2"));

        Assert.Equal("ahmed.mohamed3", Assert.Single(result.Suggestions).Value);
        Assert.Equal(["ahmed.mohamed4", "ahmed.mohamed5", "ahmed.mohamed6"], result.Alternatives);
    }

    [Fact]
    public async Task Suggest_ComparesCaseInsensitively() // G17
    {
        var result = await Suggester.SuggestAsync("Ahmed Mohamed", null, Taken("Ahmed.Mohamed"));

        Assert.Equal("ahmed.mohamed2", Assert.Single(result.Suggestions).Value);
    }

    [Fact]
    public async Task Suggest_KeepsBasePlusSuffixWithinThirtyTwoCharacters() // G18
    {
        var longNames = "Abcdefghijklmnopqrst Uvwxyzabcdefghijklm";
        var all = new List<string>();
        var result = await Suggester.SuggestAsync(longNames, null, value =>
        {
            all.Add(value);
            return Task.FromResult(value.Length >= 32 && all.Count < 3);
        });

        Assert.All(all, value => Assert.True(value.Length <= 32, value));
        Assert.NotEmpty(result.Suggestions);
        Assert.True(Assert.Single(result.Suggestions).Value.Length <= 32);
    }

    [Fact]
    public async Task Suggest_RefusesAReservedBase()
    {
        var result = await Suggester.SuggestAsync("Admin", null, Nobody);

        Assert.Empty(result.Suggestions);
        Assert.Equal("name_unusable", result.Reason);
    }

    [Theory]
    [InlineData("Ahmed.Mohamed", "ahmed.mohamed")] // V1
    [InlineData("admin_x", "admin_x")] // V9
    [InlineData("engineers", "engineers")] // V9
    [InlineData("a-b", "a-b")]
    public void Validate_AcceptsAndCanonicalises(string candidate, string expected)
    {
        var result = new UsernamePolicy(["Engineer"]).Validate(candidate);

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Canonical);
    }

    [Theory]
    [InlineData("ab", "too_short")] // V2
    [InlineData("", "too_short")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg", "too_long")] // V3, 33 characters
    [InlineData("ahmed mohamed", "whitespace")] // V4
    [InlineData(" ahmed", "whitespace")]
    [InlineData("ahmed\t", "whitespace")]
    [InlineData("ahmed@x.com", "bad_characters")] // V5
    [InlineData("أحمد1", "bad_characters")]
    [InlineData("1ahmed", "bad_shape")] // V6
    [InlineData("a..b", "bad_shape")]
    [InlineData("a._b", "bad_shape")]
    [InlineData("ahmed-", "bad_shape")]
    [InlineData(".ahmed", "bad_shape")]
    [InlineData("user123456789", "phone_like")] // V7
    [InlineData("a12345b", "valid")]
    [InlineData("admin", "reserved")] // V8
    [InlineData("Admin", "reserved")]
    [InlineData("admin2", "reserved")]
    [InlineData("root99", "reserved")]
    [InlineData("engineer", "reserved")]
    [InlineData("ENGINEER", "reserved")]
    [InlineData("accountant", "reserved")]
    [InlineData("admin.boot", "reserved")]
    public void Validate_GivesTheDocumentedReason(string candidate, string expectedReason)
    {
        var result = new UsernamePolicy(["Engineer", "Accountant", "Admin.Boot"]).Validate(candidate);

        if (expectedReason == "valid")
        {
            Assert.True(result.IsValid);
            return;
        }

        Assert.False(result.IsValid);
        Assert.Equal(expectedReason, UsernameValidation.ReasonCode(result.Reason!.Value));
    }

    [Fact]
    public async Task Alternatives_AreFreeAndValid()
    {
        var alternatives = await Suggester.AlternativesAsync("ahmed.mohamed", Taken("ahmed.mohamed", "ahmed.mohamed2"));

        Assert.Equal(["ahmed.mohamed3", "ahmed.mohamed4", "ahmed.mohamed5"], alternatives);
    }
}
