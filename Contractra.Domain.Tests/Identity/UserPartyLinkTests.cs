using System.Reflection;
using Contractra.Domain.Identity;
using Contractra.Domain.Parties;

namespace Contractra.Domain.Tests.Identity;

public class UserPartyLinkTests
{
    private static User NewUser(string login = "Engineer.Ahmed") => new(new LoginIdentifier(login), UserStatus.Active, TestUsers.Hash, mustChangePassword: false);

    private static Party NewParty(string name = "م. أحمد") => new(name, PartyStatus.Active);

    [Fact]
    public void User_LinkToParty_CreatesLink()
    {
        var user = NewUser();
        var party = NewParty();

        var link = user.LinkToParty(party);

        Assert.Same(link, user.PartyLink);
        Assert.Equal(user.Id, link.UserId);
        Assert.Equal(party.Id, link.PartyId);
        Assert.Equal('7', link.Id.ToString()[14]);
    }

    [Fact]
    public void User_CanExist_WithNoPartyLink()
    {
        Assert.Null(NewUser().PartyLink);
    }

    [Fact]
    public void User_LinkToParty_RejectsNullParty()
    {
        Assert.Throws<ArgumentNullException>(() => NewUser().LinkToParty(null!));
    }

    [Fact]
    public void User_LinkToParty_Throws_WhenAlreadyLinkedToAnotherParty()
    {
        var user = NewUser();
        var first = user.LinkToParty(NewParty("م. أحمد"));

        Assert.Throws<InvalidOperationException>(() => user.LinkToParty(NewParty("م. محمود")));
        Assert.Same(first, user.PartyLink);
    }

    [Fact]
    public void User_LinkToParty_Throws_WhenAlreadyLinkedToTheSameParty()
    {
        var user = NewUser();
        var party = NewParty();
        var first = user.LinkToParty(party);

        Assert.Throws<InvalidOperationException>(() => user.LinkToParty(party));
        Assert.Same(first, user.PartyLink);
    }

    [Fact]
    public void OnePerPartyUniqueness_IsNotVisibleInsideOneUser_SoTheDatabaseEnforcesIt()
    {
        // Owner decision R30 (2026-10-01): one account per Party. A User aggregate
        // only sees its own link, so it cannot detect another user's link to the
        // same party; ux_user_party_links_party_id is the guard
        // (UserPartyLinkIntegrityTests.TwoUsers_CannotLinkToTheSameParty). The
        // 2026-09-26 decision this test used to document is reversed.
        var party = NewParty();

        var oldAccount = NewUser("Engineer.Ahmed.Old").LinkToParty(party);
        var newAccount = NewUser("Engineer.Ahmed").LinkToParty(party);

        Assert.Equal(oldAccount.PartyId, newAccount.PartyId);
        Assert.NotEqual(oldAccount.UserId, newAccount.UserId);
    }

    [Fact]
    public void UserPartyLink_HasNoPublicConstructor_SoItIsCreatedOnlyThroughUserLinkToParty()
    {
        Assert.Empty(typeof(UserPartyLink).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void UserPartyLink_HasNoUnlinkDeleteOrRemoveMethod()
    {
        var methodNames = typeof(UserPartyLink).GetMethods().Select(m => m.Name)
            .Concat(typeof(User).GetMethods().Select(m => m.Name));

        Assert.DoesNotContain(methodNames, name =>
            name.Contains("Unlink", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Remove", StringComparison.OrdinalIgnoreCase));
    }
}
