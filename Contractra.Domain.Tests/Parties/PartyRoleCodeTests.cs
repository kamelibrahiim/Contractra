using Contractra.Domain.Parties;

namespace Contractra.Domain.Tests.Parties;

public class PartyRoleCodeTests
{
    [Fact]
    public void Constructor_Throws_ForNull()
    {
        Assert.Throws<ArgumentException>(() => new PartyRoleCode(null!));
    }

    [Fact]
    public void Constructor_Throws_ForEmpty()
    {
        Assert.Throws<ArgumentException>(() => new PartyRoleCode(string.Empty));
    }

    [Fact]
    public void Constructor_Throws_ForWhitespaceOnly()
    {
        Assert.Throws<ArgumentException>(() => new PartyRoleCode("   "));
    }

    [Fact]
    public void Constructor_Accepts_AnyNonEmptyValue()
    {
        var roleCode = new PartyRoleCode("Engineer");

        Assert.Equal("Engineer", roleCode.Value);
    }
}
