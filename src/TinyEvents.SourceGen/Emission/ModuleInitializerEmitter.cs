using TinyEvents.SourceGen.Emission.Writing;

namespace TinyEvents.SourceGen.Emission;

internal static class ModuleInitializerEmitter
{
    public static void Emit(SourceWriter writer)
    {
        writer.WriteLine("internal static class TinyEventsGeneratedModuleInitializer");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        writer.WriteLine("internal static void Initialize()");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine("global::TinyEvents.TinyEventsBootstrap.AddContribution(new TinyEventsGeneratedContribution());");
        writer.Unindent();
        writer.WriteLine("}");
        writer.Unindent();
        writer.WriteLine("}");
    }
}
