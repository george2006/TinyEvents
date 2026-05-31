namespace TinyEvents.SourceGen.Model;

internal sealed class DiscoveredConsumer
{
    public DiscoveredConsumer(
        string implementationTypeName,
        string eventTypeName,
        string eventTypeDisplayName)
    {
        ImplementationTypeName = implementationTypeName;
        EventTypeName = eventTypeName;
        EventTypeDisplayName = eventTypeDisplayName;
    }

    public string ImplementationTypeName { get; }

    public string EventTypeName { get; }

    public string EventTypeDisplayName { get; }
}
