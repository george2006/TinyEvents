namespace TinyEvents;

internal static class TinyEventTypeName
{
    public static string Get(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return eventType.FullName ?? eventType.Name;
    }
}
