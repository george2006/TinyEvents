namespace TinyEvents.SourceGen.Model;

internal sealed class DiscoveredConsumer
{
    public DiscoveredConsumer(
        string implementationTypeName,
        string eventTypeName)
    {
        ImplementationTypeName = implementationTypeName;
        EventTypeName = eventTypeName;
    }

    public string ImplementationTypeName { get; }

    public string EventTypeName { get; }

}
