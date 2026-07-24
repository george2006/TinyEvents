namespace TinyEvents;

public sealed class TinyOutboxProcessor : ITinyOutboxProcessor
{
    private readonly IServiceProvider serviceProvider;
    private readonly ITinyOutboxStore store;
    private readonly ITinyEventSerializer serializer;
    private readonly Dictionary<string, ITinyEventDispatcher> dispatchers;
    private readonly TinyEventsOptions options;
    private readonly TimeProvider timeProvider;

    public TinyOutboxProcessor(
        IServiceProvider serviceProvider,
        ITinyOutboxStore store,
        ITinyEventSerializer serializer,
        IEnumerable<ITinyEventDispatcher> dispatchers,
        TinyEventsOptions options,
        TimeProvider timeProvider)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        if (store is null)
        {
            throw new ArgumentNullException(nameof(store));
        }

        if (serializer is null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        if (dispatchers is null)
        {
            throw new ArgumentNullException(nameof(dispatchers));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (timeProvider is null)
        {
            throw new ArgumentNullException(nameof(timeProvider));
        }

        this.serviceProvider = serviceProvider;
        this.store = store;
        this.serializer = serializer;
        this.dispatchers = BuildDispatcherMap(dispatchers);
        this.options = options;
        this.timeProvider = timeProvider;
    }

    public async ValueTask ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var workerId = options.GetWorkerId();

        var messages = await store.ClaimPendingAsync(
            options.BatchSize,
            workerId,
            now,
            options.ClaimTimeout,
            cancellationToken);

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessMessageAsync(message, workerId, cancellationToken);
        }
    }

    private async ValueTask ProcessMessageAsync(
        TinyOutboxMessage message,
        string workerId,
        CancellationToken cancellationToken)
    {
        try
        {
            await InvokeConsumersAsync(message, cancellationToken);
            await MarkProcessedAsync(message, workerId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await MarkFailedAsync(message, workerId, exception, cancellationToken);
        }
    }

    private async ValueTask InvokeConsumersAsync(
        TinyOutboxMessage message,
        CancellationToken cancellationToken)
    {
        var dispatcher = ResolveDispatcher(message);
        var eventInstance = serializer.Deserialize(message.Payload, dispatcher.EventType);
        await dispatcher.DispatchAsync(serviceProvider, eventInstance, cancellationToken);
    }

    private ITinyEventDispatcher ResolveDispatcher(TinyOutboxMessage message)
    {
        if (dispatchers.TryGetValue(message.EventType, out var dispatcher))
        {
            return dispatcher;
        }

        throw new InvalidOperationException($"Event type '{message.EventType}' is not registered.");
    }

    private async ValueTask MarkProcessedAsync(
        TinyOutboxMessage message,
        string workerId,
        CancellationToken cancellationToken)
    {
        await store.MarkProcessedAsync(
            message.Id,
            workerId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async ValueTask MarkFailedAsync(
        TinyOutboxMessage message,
        string workerId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var attemptCount = message.AttemptCount + 1;
        var nextAttemptAtUtc = GetNextAttemptAtUtc(attemptCount);

        await store.MarkFailedAsync(
            message.Id,
            workerId,
            exception.Message,
            attemptCount,
            nextAttemptAtUtc,
            cancellationToken);
    }

    private DateTimeOffset? GetNextAttemptAtUtc(int attemptCount)
    {
        if (attemptCount >= options.MaxAttempts)
        {
            return null;
        }

        return timeProvider.GetUtcNow().Add(options.RetryDelay);
    }

    private static Dictionary<string, ITinyEventDispatcher> BuildDispatcherMap(IEnumerable<ITinyEventDispatcher> dispatchers)
    {
        var dispatcherMap = new Dictionary<string, ITinyEventDispatcher>(StringComparer.Ordinal);

        foreach (var dispatcher in dispatchers)
        {
            AddDispatcher(dispatcherMap, dispatcher);
        }

        return dispatcherMap;
    }

    private static void AddDispatcher(
        Dictionary<string, ITinyEventDispatcher> dispatchers,
        ITinyEventDispatcher dispatcher)
    {
        if (dispatchers.TryGetValue(dispatcher.EventTypeName, out var existingDispatcher))
        {
            if (existingDispatcher.EventType == dispatcher.EventType)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Event type name '{dispatcher.EventTypeName}' is registered for both '{existingDispatcher.EventType.FullName}' and '{dispatcher.EventType.FullName}'.");
        }

        dispatchers.Add(dispatcher.EventTypeName, dispatcher);
    }
}
