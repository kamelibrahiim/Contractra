using Microsoft.Extensions.Logging;

namespace Contractra.Application.Tests.Identity.Fakes;

/// <summary>Captures every rendered log line and every argument passed to the logger, so tests can assert secrets never reach it.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var text = formatter(state, exception);
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            text += " | " + string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}"));
        }

        Entries.Add((logLevel, text, exception));
    }
}
