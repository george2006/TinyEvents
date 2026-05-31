namespace TinyEvents;

public sealed class TinyEventPublisher : ITinyEventPublisher
{
    private readonly ITinyOutboxWriter writer;
    private readonly ITinyEventSerializer serializer;
    private readonly TimeProvider timeProvider;

    public TinyEventPublisher(
        ITinyOutboxWriter writer,
        ITinyEventSerializer serializer,
        TimeProvider timeProvider)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (serializer is null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        if (timeProvider is null)
        {
            throw new ArgumentNullException(nameof(timeProvider));
        }

        this.writer = writer;
        this.serializer = serializer;
        this.timeProvider = timeProvider;
    }

    public async ValueTask PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
    {
        if (@event is null)
        {
            throw new ArgumentNullException(nameof(@event));
        }

        var eventType = @event.GetType();
        var message = CreateMessage(@event, eventType);

        await writer.AddAsync(message, cancellationToken);
    }

    private TinyOutboxMessage CreateMessage(object @event, Type eventType)
    {
        return new TinyOutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = GetEventTypeName(eventType),
            Payload = serializer.Serialize(@event, eventType),
            Status = TinyOutboxMessageStatus.Pending,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
    }

    private static string GetEventTypeName(Type eventType)
    {
        return eventType.FullName ?? eventType.Name;
    }
}
