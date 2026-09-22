namespace Contractra.Domain.Parties;

public sealed class PartyAlias
{
    public Guid Id { get; private set; }

    public Guid PartyId { get; private set; }

    public string Alias { get; private set; } = null!;

    private PartyAlias()
    {
    }

    public PartyAlias(Guid partyId, string alias)
        : this(Guid.CreateVersion7(), partyId, alias)
    {
    }

    public PartyAlias(Guid id, Guid partyId, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("Alias cannot be null, empty, or whitespace.", nameof(alias));
        }

        Id = id;
        PartyId = partyId;
        Alias = alias;
    }
}
