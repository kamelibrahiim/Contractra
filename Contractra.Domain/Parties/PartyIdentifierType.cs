namespace Contractra.Domain.Parties;

public readonly record struct PartyIdentifierType
{
    public string Value { get; }

    public PartyIdentifierType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Party identifier type cannot be null, empty, or whitespace.", nameof(value));
        }

        Value = value;
    }

    public override string ToString() => Value;
}
