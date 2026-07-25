using System.Collections.Generic;

namespace TinyEvents.SourceGen.Planning;

internal sealed class TinyEventsGenerationPlan
{
    public TinyEventsGenerationPlan(
        IReadOnlyList<ConsumerRegistrationPlan> consumerRegistrations,
        IReadOnlyList<EventDispatcherPlan> eventDispatchers)
    {
        ConsumerRegistrations = consumerRegistrations;
        EventDispatchers = eventDispatchers;
    }

    public IReadOnlyList<ConsumerRegistrationPlan> ConsumerRegistrations { get; }

    public IReadOnlyList<EventDispatcherPlan> EventDispatchers { get; }

    public bool HasContent => ConsumerRegistrations.Count > 0 || EventDispatchers.Count > 0;
}
