using Contractra.Domain.Parties;

namespace Contractra.Domain.Tests.Parties;

public class PartyIdentifierTests
{
    private static readonly Guid SamplePartyId = Guid.CreateVersion7();
    private static readonly PartyIdentifierType SampleType = new("CommercialRegistration");

    [Fact]
    public void Constructor_StartsWithValidToNull()
    {
        var identifier = new PartyIdentifier(SamplePartyId, SampleType, "3660");

        Assert.Null(identifier.ValidTo);
    }

    [Fact]
    public void Close_WithAPastDate_Succeeds()
    {
        var identifier = new PartyIdentifier(SamplePartyId, SampleType, "3660");
        var asOf = new DateOnly(2026, 9, 22);
        var closedOn = new DateOnly(2026, 9, 1);

        identifier.Close(closedOn, asOf);

        Assert.Equal(closedOn, identifier.ValidTo);
    }

    [Fact]
    public void Close_OnTheAsOfDate_Succeeds()
    {
        var identifier = new PartyIdentifier(SamplePartyId, SampleType, "3660");
        var asOf = new DateOnly(2026, 9, 22);

        identifier.Close(asOf, asOf);

        Assert.Equal(asOf, identifier.ValidTo);
    }

    [Fact]
    public void Close_WithADateAfterAsOf_IsRejected()
    {
        var identifier = new PartyIdentifier(SamplePartyId, SampleType, "3660");
        var asOf = new DateOnly(2026, 9, 22);
        var closedOn = new DateOnly(2026, 9, 23);

        Assert.Throws<ArgumentException>(() => identifier.Close(closedOn, asOf));
        Assert.Null(identifier.ValidTo);
    }
}
