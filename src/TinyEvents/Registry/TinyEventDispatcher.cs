using Microsoft.Extensions.DependencyInjection;

namespace TinyEvents;

public sealed class TinyEventDispatcher<TEvent> : ITinyEventDispatcher
{
    public TinyEventDispatcher()
        : this(TinyEventTypeName.Get(typeof(TEvent)))
    {
    }

    public TinyEventDispatcher(string eventTypeName)
    {
        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            throw new ArgumentException("Event type name is required.", nameof(eventTypeName));
        }

        EventTypeName = eventTypeName;
    }

    public string EventTypeName { get; }

    public Type EventType => typeof(TEvent);

    public async ValueTask DispatchAsync(
        IServiceProvider serviceProvider,
        object eventInstance,
        CancellationToken cancellationToken)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        var typedEvent = (TEvent)eventInstance;
        var consumers = serviceProvider.GetServices<IEventConsumer<TEvent>>();

        foreach (var consumer in consumers)
        {
            await consumer.ConsumeAsync(typedEvent, cancellationToken);
        }
    }
}
