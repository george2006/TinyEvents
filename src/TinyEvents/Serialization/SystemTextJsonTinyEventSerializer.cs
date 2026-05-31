using System.Text.Json;

namespace TinyEvents;

public sealed class SystemTextJsonTinyEventSerializer : ITinyEventSerializer
{
    private readonly JsonSerializerOptions options;

    public SystemTextJsonTinyEventSerializer()
        : this(new JsonSerializerOptions(JsonSerializerDefaults.Web))
    {
    }

    public SystemTextJsonTinyEventSerializer(JsonSerializerOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        this.options = options;
    }

    public string Serialize(object @event, Type eventType)
    {
        if (@event is null)
        {
            throw new ArgumentNullException(nameof(@event));
        }

        if (eventType is null)
        {
            throw new ArgumentNullException(nameof(eventType));
        }

        return JsonSerializer.Serialize(@event, eventType, options);
    }

    public object Deserialize(string payload, Type eventType)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (eventType is null)
        {
            throw new ArgumentNullException(nameof(eventType));
        }

        var result = JsonSerializer.Deserialize(payload, eventType, options);

        if (result is null)
        {
            throw new InvalidOperationException($"Event payload for '{eventType.FullName}' deserialized to null.");
        }

        return result;
    }
}

