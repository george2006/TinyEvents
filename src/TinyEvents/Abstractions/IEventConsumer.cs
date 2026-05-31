namespace TinyEvents;

public interface IEventConsumer<TEvent>
{
    ValueTask ConsumeAsync(
        TEvent @event,
        CancellationToken cancellationToken);
}

