namespace Contractra.Domain.Parties;

public sealed class PartyIdentifier
{
    public Guid Id { get; private set; }

    public Guid PartyId { get; private set; }

    public PartyIdentifierType IdentifierType { get; private set; }

    public string IdentifierValue { get; private set; } = null!;

    public string? JurisdictionCountry { get; private set; }

    public string? IssuingAuthority { get; private set; }

    /// <summary>
    /// Closure marker, not a scheduled expiry: null means currently valid/open,
    /// non-null means the identifier has been closed and is no longer current.
    /// Every identifier starts with ValidTo == null; it can only be set later,
    /// via Close(...).
    /// </summary>
    public DateOnly? ValidTo { get; private set; }

    private PartyIdentifier()
    {
    }

    public PartyIdentifier(
        Guid partyId,
        PartyIdentifierType identifierType,
        string identifierValue,
        string? jurisdictionCountry = null,
        string? issuingAuthority = null)
        : this(Guid.CreateVersion7(), partyId, identifierType, identifierValue, jurisdictionCountry, issuingAuthority)
    {
    }

    public PartyIdentifier(
        Guid id,
        Guid partyId,
        PartyIdentifierType identifierType,
        string identifierValue,
        string? jurisdictionCountry,
        string? issuingAuthority)
    {
        if (string.IsNullOrWhiteSpace(identifierValue))
        {
            throw new ArgumentException("Identifier value cannot be null, empty, or whitespace.", nameof(identifierValue));
        }

        Id = id;
        PartyId = partyId;
        IdentifierType = identifierType;
        IdentifierValue = identifierValue;
        JurisdictionCountry = jurisdictionCountry;
        IssuingAuthority = issuingAuthority;
        ValidTo = null;
    }

    /// <summary>
    /// Closes this identifier as of a caller-supplied reference date. closedOn
    /// must not be after asOf - the caller supplies "now" explicitly; this
    /// entity never reads the system clock. Not a scheduled future expiry:
    /// closedOn is recorded immediately as a historical fact, not a date to
    /// take effect later.
    /// </summary>
    public void Close(DateOnly closedOn, DateOnly asOf)
    {
        if (closedOn > asOf)
        {
            throw new ArgumentException("closedOn must not be after asOf.", nameof(closedOn));
        }

        ValidTo = closedOn;
    }
}
