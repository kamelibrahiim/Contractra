namespace Contractra.Domain.Parties;

public sealed class Party
{
    private readonly List<PartyRole> _roles = new();
    private readonly List<PartyAlias> _aliases = new();
    private readonly List<PartyIdentifier> _identifiers = new();

    public Guid Id { get; private set; }

    public PartyStatus Status { get; private set; }

    public string NormalizedName { get; private set; } = null!;

    public string? Phone { get; private set; }

    public string? Address { get; private set; }

    public IReadOnlyCollection<PartyRole> Roles => _roles.AsReadOnly();

    public IReadOnlyCollection<PartyAlias> Aliases => _aliases.AsReadOnly();

    public IReadOnlyCollection<PartyIdentifier> Identifiers => _identifiers.AsReadOnly();

    private Party()
    {
    }

    public Party(string normalizedName, PartyStatus status, string? phone = null, string? address = null)
        : this(Guid.CreateVersion7(), normalizedName, status, phone, address)
    {
    }

    public Party(Guid id, string normalizedName, PartyStatus status, string? phone = null, string? address = null)
    {
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException("Normalized name cannot be null, empty, or whitespace.", nameof(normalizedName));
        }

        Id = id;
        NormalizedName = normalizedName;
        Status = status;
        Phone = phone;
        Address = address;
    }

    public PartyRole AssignRole(PartyRoleCode roleCode, DateOnly effectiveFrom, DateOnly? effectiveTo = null)
    {
        var role = new PartyRole(Id, roleCode, effectiveFrom, effectiveTo);
        _roles.Add(role);
        return role;
    }

    public PartyAlias AddAlias(string alias)
    {
        var partyAlias = new PartyAlias(Id, alias);
        _aliases.Add(partyAlias);
        return partyAlias;
    }

    public PartyIdentifier AddIdentifier(
        PartyIdentifierType identifierType,
        string identifierValue,
        string? jurisdictionCountry = null,
        string? issuingAuthority = null)
    {
        var identifier = new PartyIdentifier(Id, identifierType, identifierValue, jurisdictionCountry, issuingAuthority);
        _identifiers.Add(identifier);
        return identifier;
    }
}
