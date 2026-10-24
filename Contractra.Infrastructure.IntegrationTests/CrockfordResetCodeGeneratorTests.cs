using Contractra.Infrastructure.Identity;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>Auth contract v1.5 §10A, R44, E1. No database needed.</summary>
public class CrockfordResetCodeGeneratorTests
{
    private readonly CrockfordResetCodeGenerator _generator = new();

    [Fact]
    public void Generate_ReturnsSixteenCharacters_GroupedInFoursWithHyphens()
    {
        var generated = _generator.Generate();

        Assert.Equal(19, generated.Code.Length); // 16 characters + 3 hyphens
        var groups = generated.Code.Split('-');
        Assert.Equal(4, groups.Length);
        Assert.All(groups, g => Assert.Equal(4, g.Length));
        Assert.All(generated.Code.Where(c => c != '-'), c => Assert.Contains(c, "0123456789ABCDEFGHJKMNPQRSTVWXYZ"));
    }

    [Fact]
    public void Generate_ReturnsA32ByteHash()
    {
        var generated = _generator.Generate();
        Assert.Equal(32, generated.Hash.Length);
    }

    [Fact]
    public void Hash_OfTheDisplayedCode_MatchesTheGeneratedHash()
    {
        var generated = _generator.Generate();
        Assert.Equal(generated.Hash, _generator.Hash(generated.Code));
    }

    [Fact]
    public void Hash_IgnoresHyphensSpacesAndCase()
    {
        var generated = _generator.Generate();
        var lowered = generated.Code.ToLowerInvariant();
        var withSpaces = lowered.Replace('-', ' ');
        var noSeparators = lowered.Replace("-", "");

        Assert.Equal(generated.Hash, _generator.Hash(lowered));
        Assert.Equal(generated.Hash, _generator.Hash(withSpaces));
        Assert.Equal(generated.Hash, _generator.Hash(noSeparators));
    }

    [Fact]
    public void Hash_OfADifferentCode_Differs()
    {
        var first = _generator.Generate();
        var second = _generator.Generate();
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void Generate_ProducesDifferentCodes()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => _generator.Generate().Code).ToHashSet();
        Assert.Equal(50, codes.Count);
    }
}
