using Microsoft.Extensions.Logging;

namespace TinyEvents.Testing;

internal sealed class RecordingLogger : ILogger
{
    internal List<RecordedLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> values
            ? values.ToDictionary(value => value.Key, value => value.Value)
            : new Dictionary<string, object?>();

        Entries.Add(
            new RecordedLogEntry(
                logLevel,
                eventId,
                properties,
                exception,
                formatter(state, exception)));
    }
}

internal sealed record RecordedLogEntry(
    LogLevel Level,
    EventId EventId,
    IReadOnlyDictionary<string, object?> Properties,
    Exception? Exception,
    string Message);
