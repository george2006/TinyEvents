namespace TinyEvents.SourceGen.Planning;

internal sealed class EventDispatcherPlan
{
    public EventDispatcherPlan(string eventTypeName)
    {
        EventTypeName = eventTypeName;
    }

    public string EventTypeName { get; }

}
