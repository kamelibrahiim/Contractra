namespace Contractra.Domain.Identity;

/// <summary>
/// Links a login account to its commercial Party - an engineer is both a user
/// and a party. Owned by User; created only through User.LinkToParty. A user
/// links to at most one party. A party may be linked from more than one user
/// (owner decision), e.g. a disabled account and its replacement. There is no
/// unlink operation: no lifecycle is documented for this link.
/// </summary>
public sealed class UserPartyLink
{
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid PartyId { get; private set; }

    private UserPartyLink()
    {
    }

    internal UserPartyLink(Guid userId, Guid partyId)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        PartyId = partyId;
    }
}
