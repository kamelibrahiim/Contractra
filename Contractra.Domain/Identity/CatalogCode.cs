namespace Contractra.Domain.Identity;

/// <summary>
/// Shared validation for Identity value objects backed by a plain string
/// business identifier (e.g. RoleCode, PermissionCode, LoginIdentifier).
/// </summary>
internal static class CatalogCode
{
    public static void Validate(string? value, string description, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{description} cannot be null, empty, or whitespace.", parameterName);
        }

        // Rejected rather than trimmed: silently changing an identifier would
        // hide the fact that the caller supplied a different value.
        if (value.Length != value.Trim().Length)
        {
            throw new ArgumentException($"{description} cannot have leading or trailing whitespace.", parameterName);
        }
    }
}
