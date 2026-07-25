namespace TinyEvents.SourceGen.Planning;

internal sealed class EventDispatcherPlan
{
    public EventDispatcherPlan(
        string eventTypeName,
        string eventTypeDisplayName)
    {
        EventTypeName = eventTypeName;
        EventTypeDisplayName = eventTypeDisplayName;
    }

    public string EventTypeName { get; }

    public string EventTypeDisplayName { get; }
}
