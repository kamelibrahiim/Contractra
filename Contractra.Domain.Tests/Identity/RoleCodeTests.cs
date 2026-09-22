using Contractra.Domain.Identity;

namespace Contractra.Domain.Tests.Identity;

public class RoleCodeTests
{
    [Fact]
    public void Constructor_Throws_ForNull()
    {
        Assert.Throws<ArgumentException>(() => new RoleCode(null!));
    }

    [Fact]
    public void Constructor_Throws_ForEmpty()
    {
        Assert.Throws<ArgumentException>(() => new RoleCode(string.Empty));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Constructor_Throws_ForWhitespaceOnly(string value)
    {
        Assert.Throws<ArgumentException>(() => new RoleCode(value));
    }

    [Theory]
    [InlineData(" Admin")]
    [InlineData("Admin ")]
    [InlineData("\tAdmin")]
    [InlineData("Admin\n")]
    public void Constructor_Throws_ForLeadingOrTrailingWhitespace(string value)
    {
        Assert.Throws<ArgumentException>(() => new RoleCode(value));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("OfficeStaff")]
    [InlineData("office staff")]
    public void Constructor_PreservesTheSuppliedValueExactly(string value)
    {
        var code = new RoleCode(value);

        Assert.Equal(value, code.Value);
    }
}
