using System.Collections.Generic;

namespace TinyEvents.SourceGen.Planning;

internal sealed class TinyEventsGenerationPlan
{
    public TinyEventsGenerationPlan(
        IReadOnlyList<ConsumerRegistrationPlan> consumerRegistrations,
        IReadOnlyList<EventTypeDescriptorPlan> eventTypeDescriptors)
    {
        ConsumerRegistrations = consumerRegistrations;
        EventTypeDescriptors = eventTypeDescriptors;
    }

    public IReadOnlyList<ConsumerRegistrationPlan> ConsumerRegistrations { get; }

    public IReadOnlyList<EventTypeDescriptorPlan> EventTypeDescriptors { get; }

    public bool HasContent => ConsumerRegistrations.Count > 0 || EventTypeDescriptors.Count > 0;
}
