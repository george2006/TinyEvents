using TinyEvents.SourceGen.Emission.Writing;
using TinyEvents.SourceGen.Planning;

namespace TinyEvents.SourceGen.Emission;

internal static class EventDispatcherEmitter
{
    public static void Emit(
        SourceWriter writer,
        EventDispatcherPlan dispatcher)
    {
        writer.Write("global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<global::TinyEvents.ITinyEventDispatcher>(services, new global::TinyEvents.TinyEventDispatcher<");
        writer.Write(dispatcher.EventTypeName);
        writer.WriteLine(">());");
    }
}
