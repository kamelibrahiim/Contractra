using Contractra.Infrastructure.Identity;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>Auth contract v1.5 §10, R2, E4. No database needed.</summary>
public class SecureTemporaryPasswordGeneratorTests
{
    private readonly SecureTemporaryPasswordGenerator _generator = new();

    [Fact]
    public void Generate_ReturnsTwelveCharacters_FromTheAllowedAlphabetOnly()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = _generator.Generate();
            Assert.Equal(12, password.Length);
            Assert.All(password, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
        }
    }

    [Fact]
    public void Generate_AlwaysContainsAnUpperCaseLetterALowerCaseLetterAndADigit()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = _generator.Generate();
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
        }
    }

    [Fact]
    public void Generate_ProducesDifferentValues()
    {
        var passwords = Enumerable.Range(0, 50).Select(_ => _generator.Generate()).ToHashSet();
        Assert.Equal(50, passwords.Count);
    }
}
