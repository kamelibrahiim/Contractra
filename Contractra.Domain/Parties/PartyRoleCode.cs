namespace Contractra.Domain.Parties;

public readonly record struct PartyRoleCode
{
    public string Value { get; }

    public PartyRoleCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Party role code cannot be null, empty, or whitespace.", nameof(value));
        }

        Value = value;
    }

    public override string ToString() => Value;
}
