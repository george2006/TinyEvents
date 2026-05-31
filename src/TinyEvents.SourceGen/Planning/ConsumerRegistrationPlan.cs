namespace TinyEvents.SourceGen.Planning;

internal sealed class ConsumerRegistrationPlan
{
    public ConsumerRegistrationPlan(
        string implementationTypeName,
        string eventTypeName)
    {
        ImplementationTypeName = implementationTypeName;
        EventTypeName = eventTypeName;
    }

    public string ImplementationTypeName { get; }

    public string EventTypeName { get; }
}
