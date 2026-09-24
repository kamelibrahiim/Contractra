namespace Contractra.Domain.Identity;

/// <summary>
/// Business identifier a user logs in with. Deliberately opaque: no email,
/// username, or phone format is imposed - none is confirmed. The supplied
/// casing and value are preserved exactly.
/// </summary>
public readonly record struct LoginIdentifier
{
    public string Value { get; }

    public LoginIdentifier(string value)
    {
        CatalogCode.Validate(value, "Login identifier", nameof(value));
        Value = value;
    }

    public override string ToString() => Value;
}
