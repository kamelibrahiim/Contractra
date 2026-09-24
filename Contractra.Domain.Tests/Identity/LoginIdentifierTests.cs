using Contractra.Domain.Identity;

namespace Contractra.Domain.Tests.Identity;

public class LoginIdentifierTests
{
    [Fact]
    public void Constructor_Throws_ForNull()
    {
        Assert.Throws<ArgumentException>(() => new LoginIdentifier(null!));
    }

    [Fact]
    public void Constructor_Throws_ForEmpty()
    {
        Assert.Throws<ArgumentException>(() => new LoginIdentifier(string.Empty));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Constructor_Throws_ForWhitespaceOnly(string value)
    {
        Assert.Throws<ArgumentException>(() => new LoginIdentifier(value));
    }

    [Theory]
    [InlineData(" admin")]
    [InlineData("\tadmin")]
    public void Constructor_Throws_ForLeadingWhitespace(string value)
    {
        Assert.Throws<ArgumentException>(() => new LoginIdentifier(value));
    }

    [Theory]
    [InlineData("admin ")]
    [InlineData("admin\t")]
    [InlineData("admin\n")]
    public void Constructor_Throws_ForTrailingWhitespace(string value)
    {
        Assert.Throws<ArgumentException>(() => new LoginIdentifier(value));
    }

    [Fact]
    public void Constructor_Throws_ForLeadingAndTrailingWhitespace()
    {
        Assert.Throws<ArgumentException>(() => new LoginIdentifier(" admin "));
    }

    [Theory]
    [InlineData("Admin.User01")]
    [InlineData("admin@example.com")]
    [InlineData("office staff")]
    public void Constructor_PreservesTheSuppliedValueExactly(string value)
    {
        var identifier = new LoginIdentifier(value);

        Assert.Equal(value, identifier.Value);
    }
}
