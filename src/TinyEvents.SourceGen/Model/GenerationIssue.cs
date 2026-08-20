namespace TinyEvents.SourceGen.Model;

internal sealed class GenerationIssue
{
    private GenerationIssue(
        string id,
        string message)
    {
        Id = id;
        Message = message;
    }

    public string Id { get; }

    public string Message { get; }

    public static GenerationIssue OpenGenericConsumer(string consumerTypeName)
    {
        return new GenerationIssue(
            "TEV001",
            $"Open generic event consumer '{consumerTypeName}' is not supported by TinyEvents source generation.");
    }

    public static GenerationIssue GenericEventContract(
        string consumerTypeName,
        string eventTypeName)
    {
        return new GenerationIssue(
            "TEV002",
            $"Event consumer '{consumerTypeName}' uses generic event contract '{eventTypeName}'. TinyEvents does not support generic event contracts. Use a dedicated non-generic event type.");
    }
}
