namespace TinyEvents;

public interface ITinyEventSerializer
{
    string Serialize(object @event, Type eventType);

    object Deserialize(string payload, Type eventType);
}

