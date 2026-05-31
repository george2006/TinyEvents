using TinyEvents.SourceGen.Emission.Writing;
using TinyEvents.SourceGen.Planning;

namespace TinyEvents.SourceGen.Emission;

internal static class ConsumerRegistrationEmitter
{
    public static void Emit(
        SourceWriter writer,
        TinyEventsGenerationPlan plan)
    {
        writer.WriteLine("internal sealed class TinyEventsGeneratedContribution : global::TinyEvents.ITinyEventsContribution");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("public void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        writer.WriteLine("{");
        writer.Indent();

        foreach (var registration in plan.ConsumerRegistrations)
        {
            WriteConsumerRegistration(writer, registration);
        }

        foreach (var descriptor in plan.EventTypeDescriptors)
        {
            EventTypeDescriptorEmitter.Emit(writer, descriptor);
        }

        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
    }

    private static void WriteConsumerRegistration(
        SourceWriter writer,
        ConsumerRegistrationPlan registration)
    {
        writer.Write("global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<global::TinyEvents.IEventConsumer<");
        writer.Write(registration.EventTypeName);
        writer.Write(">, ");
        writer.Write(registration.ImplementationTypeName);
        writer.WriteLine(">(services);");
    }
}
