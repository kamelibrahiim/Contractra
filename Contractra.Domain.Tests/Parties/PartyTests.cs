using Contractra.Domain.Parties;

namespace Contractra.Domain.Tests.Parties;

public class PartyTests
{
    [Theory]
    [InlineData(PartyStatus.Active)]
    [InlineData(PartyStatus.NeedsClassification)]
    [InlineData(PartyStatus.Inactive)]
    public void Constructor_AcceptsEachDocumentedStatusValue(PartyStatus status)
    {
        var party = new Party("Test Party", status);

        Assert.Equal(status, party.Status);
    }

    [Fact]
    public void Constructor_RequiresNormalizedName()
    {
        Assert.Throws<ArgumentException>(() => new Party(string.Empty, PartyStatus.Active));
        Assert.Throws<ArgumentException>(() => new Party("   ", PartyStatus.Active));
    }

    [Fact]
    public void Constructor_GeneratesAVersion7Guid_WhenNoIdIsProvided()
    {
        var party = new Party("Test Party", PartyStatus.Active);

        // UUID v7: the version nibble (position 14, 0-indexed) must be '7'.
        Assert.Equal('7', party.Id.ToString()[14]);
    }

    [Fact]
    public void Party_CanExist_WithZeroRoles()
    {
        var party = new Party("Historical Name", PartyStatus.NeedsClassification);

        Assert.Empty(party.Roles);
    }

    [Fact]
    public void AssignRole_AddsARoleOwnedByThisParty()
    {
        var party = new Party("Engineer Party", PartyStatus.Active);

        var role = party.AssignRole(new PartyRoleCode("Engineer"), new DateOnly(2026, 1, 1));

        Assert.Single(party.Roles);
        Assert.Equal(party.Id, role.PartyId);
    }

    [Fact]
    public void AddAlias_AddsAnAliasOwnedByThisParty()
    {
        var party = new Party("Original Name", PartyStatus.Active);

        var alias = party.AddAlias("Alternate Spelling");

        Assert.Single(party.Aliases);
        Assert.Equal(party.Id, alias.PartyId);
    }

    [Fact]
    public void AddIdentifier_AddsAnIdentifierOwnedByThisParty_WithNoValidFromField()
    {
        var party = new Party("Company", PartyStatus.Active);

        var identifier = party.AddIdentifier(new PartyIdentifierType("CommercialRegistration"), "3660");

        Assert.Single(party.Identifiers);
        Assert.Equal(party.Id, identifier.PartyId);
        Assert.Null(identifier.ValidTo);
    }
}
