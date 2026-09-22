using Contractra.Domain.Parties;

namespace Contractra.Domain.Tests.Parties;

public class PartyRoleTests
{
    private static readonly Guid SamplePartyId = Guid.CreateVersion7();
    private static readonly PartyRoleCode SampleRole = new("Engineer");

    [Fact]
    public void Constructor_Throws_WhenEffectiveToIsBeforeEffectiveFrom()
    {
        var effectiveFrom = new DateOnly(2026, 9, 10);
        var effectiveTo = new DateOnly(2026, 9, 9);

        Assert.Throws<ArgumentException>(() =>
            new PartyRole(SamplePartyId, SampleRole, effectiveFrom, effectiveTo));
    }

    [Fact]
    public void Constructor_Allows_EffectiveToEqualToEffectiveFrom()
    {
        var day = new DateOnly(2026, 9, 10);

        var role = new PartyRole(SamplePartyId, SampleRole, day, day);

        Assert.Equal(day, role.EffectiveTo);
    }

    [Fact]
    public void Constructor_Allows_NullEffectiveTo_AsOpenEnded()
    {
        var role = new PartyRole(SamplePartyId, SampleRole, new DateOnly(2026, 9, 1), null);

        Assert.Null(role.EffectiveTo);
    }

    [Fact]
    public void IsActiveOn_ReturnsTrue_ForOpenEndedRole_OnAnyDateOnOrAfterStart()
    {
        var role = new PartyRole(SamplePartyId, SampleRole, new DateOnly(2026, 1, 1), null);

        Assert.True(role.IsActiveOn(new DateOnly(2026, 1, 1)));
        Assert.True(role.IsActiveOn(new DateOnly(2099, 1, 1)));
    }

    [Fact]
    public void IsActiveOn_ReturnsTrue_ForBoundedRole_WithinRange()
    {
        var role = new PartyRole(SamplePartyId, SampleRole, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.True(role.IsActiveOn(new DateOnly(2026, 9, 15)));
    }

    [Fact]
    public void IsActiveOn_ReturnsFalse_BeforeEffectiveFrom()
    {
        var role = new PartyRole(SamplePartyId, SampleRole, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.False(role.IsActiveOn(new DateOnly(2026, 8, 31)));
    }

    [Fact]
    public void IsActiveOn_ReturnsTrue_OnEffectiveToDate_BecauseTheUpperBoundIsInclusive()
    {
        var role = new PartyRole(SamplePartyId, SampleRole, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.True(role.IsActiveOn(new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public void IsActiveOn_ReturnsFalse_AfterEffectiveTo()
    {
        var role = new PartyRole(SamplePartyId, SampleRole, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.False(role.IsActiveOn(new DateOnly(2026, 10, 1)));
    }
}
