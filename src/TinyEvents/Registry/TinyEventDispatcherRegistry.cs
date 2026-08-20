namespace TinyEvents;

internal sealed class TinyEventDispatcherRegistry
{
    private readonly Dictionary<string, ITinyEventDispatcher> dispatchersByName =
        new Dictionary<string, ITinyEventDispatcher>(StringComparer.Ordinal);
    private readonly Dictionary<Type, ITinyEventDispatcher> dispatchersByType =
        new Dictionary<Type, ITinyEventDispatcher>();

    public TinyEventDispatcherRegistry(
        IEnumerable<ITinyEventDispatcher> dispatchers,
        IReadOnlyList<TinyEventNameAlias> aliases)
    {
        ArgumentNullException.ThrowIfNull(dispatchers);
        ArgumentNullException.ThrowIfNull(aliases);

        RegisterDispatchers(dispatchers);
        RegisterAliases(aliases);
    }

    public ITinyEventDispatcher Resolve(string eventTypeName)
    {
        if (dispatchersByName.TryGetValue(eventTypeName, out var dispatcher))
        {
            return dispatcher;
        }

        throw new InvalidOperationException($"Event type '{eventTypeName}' is not registered.");
    }

    private void RegisterDispatchers(IEnumerable<ITinyEventDispatcher> dispatchers)
    {
        foreach (var dispatcher in dispatchers)
        {
            RegisterDispatcher(dispatcher);
        }
    }

    private void RegisterAliases(IReadOnlyList<TinyEventNameAlias> aliases)
    {
        foreach (var alias in aliases)
        {
            RegisterAlias(alias);
        }
    }

    private void RegisterDispatcher(ITinyEventDispatcher dispatcher)
    {
        RegisterName(dispatcher.EventTypeName, dispatcher);
        dispatchersByType.TryAdd(dispatcher.EventType, dispatcher);
    }

    private void RegisterAlias(TinyEventNameAlias alias)
    {
        if (!dispatchersByType.TryGetValue(alias.EventType, out var dispatcher))
        {
            throw new InvalidOperationException(
                $"Previous event name '{alias.Name}' targets '{alias.EventType.FullName}', but no dispatcher is registered for that event type.");
        }

        RegisterName(alias.Name, dispatcher);
    }

    private void RegisterName(
        string eventTypeName,
        ITinyEventDispatcher dispatcher)
    {
        if (dispatchersByName.TryGetValue(eventTypeName, out var registeredDispatcher))
        {
            EnsureSameEventType(eventTypeName, registeredDispatcher, dispatcher);
            return;
        }

        dispatchersByName.Add(eventTypeName, dispatcher);
    }

    private static void EnsureSameEventType(
        string eventTypeName,
        ITinyEventDispatcher registeredDispatcher,
        ITinyEventDispatcher dispatcher)
    {
        if (registeredDispatcher.EventType == dispatcher.EventType)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Event type name '{eventTypeName}' is registered for both '{registeredDispatcher.EventType.FullName}' and '{dispatcher.EventType.FullName}'.");
    }
}
