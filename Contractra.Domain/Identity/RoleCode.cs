namespace Contractra.Domain.Identity;

/// <summary>
/// Business identifier of a Role. Roles are managed reference data, so this is
/// deliberately not a closed enum. The supplied casing is preserved; uniqueness
/// is case-insensitive and is enforced by the database (lower(code)).
/// No format, segment count or normalization is imposed - none is confirmed.
/// </summary>
public readonly record struct RoleCode
{
    public string Value { get; }

    public RoleCode(string value)
    {
        CatalogCode.Validate(value, "Role code", nameof(value));
        Value = value;
    }

    public override string ToString() => Value;
}
