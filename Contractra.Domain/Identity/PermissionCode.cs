namespace Contractra.Domain.Identity;

/// <summary>
/// Business identifier of a Permission. The supplied casing is preserved;
/// uniqueness is case-insensitive and is enforced by the database (lower(code)).
/// No `&lt;Domain&gt;.&lt;Entity&gt;.&lt;Action&gt;` pattern or segment count is enforced:
/// the confirmed codes themselves (e.g. "Payroll.View", "Reports.Monthly.View")
/// do not share one shape - see docs/decisions/0002-authorization-catalog-persistence.md.
/// </summary>
public readonly record struct PermissionCode
{
    /// <summary>
    /// BR-235 / ح-235: never granted through a Role, only explicitly per user.
    /// </summary>
    public const string PayrollView = "Payroll.View";

    public string Value { get; }

    public PermissionCode(string value)
    {
        CatalogCode.Validate(value, "Permission code", nameof(value));
        Value = value;
    }

    /// <summary>
    /// Case-insensitive, matching the database guard, which compares lower(code).
    /// </summary>
    public bool IsPayrollView =>
        string.Equals(Value.ToLowerInvariant(), PayrollView.ToLowerInvariant(), StringComparison.Ordinal);

    public override string ToString() => Value;
}
