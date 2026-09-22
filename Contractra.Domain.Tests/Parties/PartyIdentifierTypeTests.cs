using Contractra.Domain.Parties;

namespace Contractra.Domain.Tests.Parties;

public class PartyIdentifierTypeTests
{
    [Fact]
    public void Constructor_Throws_ForNull()
    {
        Assert.Throws<ArgumentException>(() => new PartyIdentifierType(null!));
    }

    [Fact]
    public void Constructor_Throws_ForEmpty()
    {
        Assert.Throws<ArgumentException>(() => new PartyIdentifierType(string.Empty));
    }

    [Fact]
    public void Constructor_Throws_ForWhitespaceOnly()
    {
        Assert.Throws<ArgumentException>(() => new PartyIdentifierType("   "));
    }

    [Fact]
    public void Constructor_Accepts_AnyNonEmptyValue()
    {
        var identifierType = new PartyIdentifierType("CommercialRegistration");

        Assert.Equal("CommercialRegistration", identifierType.Value);
    }
}
