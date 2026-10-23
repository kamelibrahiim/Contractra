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
    private readonly Dictionary<string, string> _typeErrors = new(StringComparer.Ordinal);

    private AuthRequestBody(BodyStatus status, JsonElement root)
    {
        Status = status;
        _root = root;
    }

    public BodyStatus Status { get; }

    /// <summary>Fields that were present with a value of the wrong type.</summary>
    public IReadOnlyCollection<string> TypeErrors => _typeErrors.Keys;

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
                    _typeErrors[name] = StringMessage;
                    return null;
            }
        }

        return null;
    }

    /// <summary>
    /// The string items of an array field; null when absent or JSON null. A value that is
    /// not an array, or an array holding a non-string item, is recorded as a type error
    /// (and yields null) - no partial list is ever returned.
    /// </summary>
    public IReadOnlyList<string>? GetStringArray(string name)
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
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Array:
                    var items = new List<string>();
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String)
                        {
                            _typeErrors[name] = StringArrayMessage;
                            return null;
                        }

                        items.Add(item.GetString()!);
                    }

                    return items;
                default:
                    _typeErrors[name] = StringArrayMessage;
                    return null;
            }
        }

        return null;
    }

    /// <summary>A string at a nested path (for example <c>whatsapp.number</c>); null when absent or JSON null; a non-string is a type error named by the dotted path.</summary>
    public string? GetStringAt(params string[] path)
    {
        var element = Walk(path, out var name);
        if (element is null)
        {
            return null;
        }

        switch (element.Value.ValueKind)
        {
            case JsonValueKind.String:
                return element.Value.GetString();
            case JsonValueKind.Null:
                return null;
            default:
                _typeErrors[name] = StringMessage;
                return null;
        }
    }

    /// <summary>A boolean at a nested path; null when absent or JSON null; a non-boolean is a type error named by the dotted path.</summary>
    public bool? GetBooleanAt(params string[] path)
    {
        var element = Walk(path, out var name);
        if (element is null)
        {
            return null;
        }

        switch (element.Value.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            default:
                _typeErrors[name] = "The value must be true or false.";
                return null;
        }
    }

    private JsonElement? Walk(string[] path, out string dottedName)
    {
        dottedName = string.Join('.', path);
        if (Status != BodyStatus.Ok)
        {
            return null;
        }

        var current = _root;
        foreach (var segment in path)
        {
            if (current.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (current.ValueKind != JsonValueKind.Object)
            {
                _typeErrors[dottedName] = "The value must be an object.";
                return null;
            }

            JsonElement? next = null;
            foreach (var property in current.EnumerateObject())
            {
                if (string.Equals(property.Name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    next = property.Value;
                    break;
                }
            }

            if (next is null)
            {
                return null;
            }

            current = next.Value;
        }

        return current;
    }

    /// <summary>Records a field-level error for a value that cannot be used (for example a malformed identifier).</summary>
    public void AddFieldError(string name, string message) => _typeErrors[name] = message;

    public Dictionary<string, string[]> TypeErrorMessages() =>
        _typeErrors.ToDictionary(entry => entry.Key, entry => new[] { entry.Value });

    private const string StringMessage = "The value must be a string.";
    private const string StringArrayMessage = "The value must be an array of strings.";
}
