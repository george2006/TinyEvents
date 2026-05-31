using TinyEvents.SourceGen.Emission.Writing;
using TinyEvents.SourceGen.Planning;

namespace TinyEvents.SourceGen.Emission;

internal static class EventTypeDescriptorEmitter
{
    public static void Emit(
        SourceWriter writer,
        EventTypeDescriptorPlan descriptor)
    {
        writer.Write("global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, new global::TinyEvents.TinyEventTypeDescriptor(");
        writer.Write(StringLiteral.From(descriptor.EventTypeDisplayName));
        writer.Write(", typeof(");
        writer.Write(descriptor.EventTypeName);
        writer.WriteLine(")));");
    }
}
