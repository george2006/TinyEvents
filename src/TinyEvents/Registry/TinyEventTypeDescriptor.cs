namespace TinyEvents;

public sealed class TinyEventTypeDescriptor
{
    public TinyEventTypeDescriptor(string eventTypeName, Type eventType)
    {
        if (string.IsNullOrWhiteSpace(eventTypeName))
        {
            throw new ArgumentException("Event type name is required.", nameof(eventTypeName));
        }

        if (eventType is null)
        {
            throw new ArgumentNullException(nameof(eventType));
        }

        EventTypeName = eventTypeName;
        EventType = eventType;
    }

    public string EventTypeName { get; }

    public Type EventType { get; }
}

