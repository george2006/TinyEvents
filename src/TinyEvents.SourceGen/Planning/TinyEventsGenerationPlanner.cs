using System;
using System.Linq;
using TinyEvents.SourceGen.Model;

namespace TinyEvents.SourceGen.Planning;

internal static class TinyEventsGenerationPlanner
{
    public static TinyEventsGenerationPlan Plan(DiscoveryResult discovery)
    {
        var registrations = discovery.Consumers
            .Select(CreateConsumerRegistration)
            .OrderBy(registration => registration.EventTypeName, StringComparer.Ordinal)
            .ThenBy(registration => registration.ImplementationTypeName, StringComparer.Ordinal)
            .ToArray();

        var dispatchers = discovery.Consumers
            .GroupBy(consumer => consumer.EventTypeName, StringComparer.Ordinal)
            .Select(group => CreateEventDispatcher(group.First()))
            .OrderBy(dispatcher => dispatcher.EventTypeName, StringComparer.Ordinal)
            .ToArray();

        return new TinyEventsGenerationPlan(registrations, dispatchers);
    }

    private static ConsumerRegistrationPlan CreateConsumerRegistration(DiscoveredConsumer consumer)
    {
        return new ConsumerRegistrationPlan(
            consumer.ImplementationTypeName,
            consumer.EventTypeName);
    }

    private static EventDispatcherPlan CreateEventDispatcher(DiscoveredConsumer consumer)
    {
        return new EventDispatcherPlan(consumer.EventTypeName);
    }
}
