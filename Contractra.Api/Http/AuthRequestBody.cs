using System.Text.Json;

namespace Contractra.Api.Http;

public enum BodyStatus
{
    Ok,
    Empty,
    TooLarge,
    Malformed,
}

/// <summary>
/// Reads an auth request body as a JSON object, refusing more than 16 KiB before
/// parsing it (contract §1, E13). Fields are looked up case-insensitively, like the
/// framework's web JSON defaults. Nothing read here is logged.
/// </summary>
public sealed class AuthRequestBody
{
    public const int MaxBytes = 16 * 1024;

    private readonly JsonElement _root;
    private readonly HashSet<string> _typeErrors = new(StringComparer.Ordinal);

    private AuthRequestBody(BodyStatus status, JsonElement root)
    {
        Status = status;
        _root = root;
    }

    public BodyStatus Status { get; }

    /// <summary>Fields that were present with a non-string value.</summary>
    public IReadOnlyCollection<string> TypeErrors => _typeErrors;

    public static async Task<AuthRequestBody> ReadAsync(HttpRequest request)
    {
        if (request.ContentLength is > MaxBytes)
        {
            return new AuthRequestBody(BodyStatus.TooLarge, default);
        }

        var buffer = new byte[MaxBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await request.Body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), request.HttpContext.RequestAborted)) > 0)
        {
            total += read;
        }

        if (total > MaxBytes)
        {
            return new AuthRequestBody(BodyStatus.TooLarge, default);
        }

        if (total == 0 || buffer.AsSpan(0, total).Trim(" \t\r\n"u8).IsEmpty)
        {
            return new AuthRequestBody(BodyStatus.Empty, default);
        }

        try
        {
            using var document = JsonDocument.Parse(buffer.AsMemory(0, total));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new AuthRequestBody(BodyStatus.Malformed, default);
            }

            return new AuthRequestBody(BodyStatus.Ok, document.RootElement.Clone());
        }
        catch (JsonException)
        {
            return new AuthRequestBody(BodyStatus.Malformed, default);
        }
    }

    /// <summary>The string value of a field; null when absent or JSON null; a non-string value is recorded as a type error.</summary>
    public string? GetString(string name)
    {
        if (Status != BodyStatus.Ok)
        {
            return null;
        }

        foreach (var property in _root.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    return property.Value.GetString();
                case JsonValueKind.Null:
                    return null;
                default:
                    _typeErrors.Add(name);
                    return null;
            }
        }

        return null;
    }

    public Dictionary<string, string[]> TypeErrorMessages() =>
        _typeErrors.ToDictionary(field => field, _ => new[] { "The value must be a string." });
}
