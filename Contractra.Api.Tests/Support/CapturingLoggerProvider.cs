namespace Contractra.Api.Tests.Support;

/// <summary>Collects every rendered log line (message, structured values, exception) so a test can prove what was and was not logged.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Category, string Text)> _entries = new();

    public IReadOnlyList<(LogLevel Level, string Category, string Text)> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public string AllText => string.Join("\n", Entries.Select(e => e.Text));

    public bool Contains(string fragment) => Entries.Any(e => e.Text.Contains(fragment, StringComparison.Ordinal));

    public void Clear()
    {
        lock (_entries)
        {
            _entries.Clear();
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly CapturingLoggerProvider _owner;
        private readonly string _category;

        public CapturingLogger(CapturingLoggerProvider owner, string category) => (_owner, _category) = (owner, category);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                text += " | " + string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}"));
            }

            if (exception is not null)
            {
                text += " | " + exception;
            }

            lock (_owner._entries)
            {
                _owner._entries.Add((logLevel, _category, text));
            }
        }
    }
}
