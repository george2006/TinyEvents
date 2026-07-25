namespace TinyEvents;

public interface ITinyEventDispatcher
{
    string EventTypeName { get; }

    Type EventType { get; }

    ValueTask DispatchAsync(
        IServiceProvider serviceProvider,
        object eventInstance,
        CancellationToken cancellationToken);
}
