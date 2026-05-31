namespace TinyEvents;

public interface ITinyEventPublisher
{
    ValueTask PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default);
}

