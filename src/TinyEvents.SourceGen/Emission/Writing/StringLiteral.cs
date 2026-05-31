namespace TinyEvents.SourceGen.Emission.Writing;

internal static class StringLiteral
{
    public static string From(string value)
    {
        return "@\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
