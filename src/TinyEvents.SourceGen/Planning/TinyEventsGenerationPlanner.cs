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

        var descriptors = discovery.Consumers
            .GroupBy(consumer => consumer.EventTypeDisplayName, StringComparer.Ordinal)
            .Select(group => CreateEventTypeDescriptor(group.First()))
            .OrderBy(descriptor => descriptor.EventTypeDisplayName, StringComparer.Ordinal)
            .ToArray();

        return new TinyEventsGenerationPlan(registrations, descriptors);
    }

    private static ConsumerRegistrationPlan CreateConsumerRegistration(DiscoveredConsumer consumer)
    {
        return new ConsumerRegistrationPlan(
            consumer.ImplementationTypeName,
            consumer.EventTypeName);
    }

    private static EventTypeDescriptorPlan CreateEventTypeDescriptor(DiscoveredConsumer consumer)
    {
        return new EventTypeDescriptorPlan(
            consumer.EventTypeName,
            consumer.EventTypeDisplayName);
    }
}
