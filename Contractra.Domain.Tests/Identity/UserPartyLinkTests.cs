using System.Reflection;
using Contractra.Domain.Identity;
using Contractra.Domain.Parties;

namespace Contractra.Domain.Tests.Identity;

public class UserPartyLinkTests
{
    private static User NewUser(string login = "Engineer.Ahmed") => new(new LoginIdentifier(login), UserStatus.Active);

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
    public void TwoUsers_MayLinkToTheSameParty()
    {
        // Owner decision (2026-09-26): party_id is not unique. For example, a
        // disabled old account and its replacement both stay linked to the same
        // engineer party, so earlier custody and expense history stays attached.
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
