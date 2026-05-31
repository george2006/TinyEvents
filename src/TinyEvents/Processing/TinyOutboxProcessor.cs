using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace TinyEvents;

public sealed class TinyOutboxProcessor : ITinyOutboxProcessor
{
    private static readonly MethodInfo ProcessTypedMessageMethod =
        typeof(TinyOutboxProcessor).GetMethod(
            nameof(ProcessTypedMessageAsync),
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Typed message processor method was not found.");

    private readonly IServiceProvider serviceProvider;
    private readonly ITinyOutboxStore store;
    private readonly ITinyEventSerializer serializer;
    private readonly Dictionary<string, Type> eventTypes;
    private readonly TinyEventsOptions options;
    private readonly TimeProvider timeProvider;

    public TinyOutboxProcessor(
        IServiceProvider serviceProvider,
        ITinyOutboxStore store,
        ITinyEventSerializer serializer,
        IEnumerable<TinyEventTypeDescriptor> eventTypes,
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

        if (eventTypes is null)
        {
            throw new ArgumentNullException(nameof(eventTypes));
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
        this.eventTypes = BuildEventTypeMap(eventTypes);
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
        var eventType = ResolveEventType(message);
        var eventInstance = serializer.Deserialize(message.Payload, eventType);
        var method = ProcessTypedMessageMethod.MakeGenericMethod(eventType);

        var result = method.Invoke(this, new[] { eventInstance, cancellationToken });

        if (result is null)
        {
            throw new InvalidOperationException("Typed event processing returned no task.");
        }

        await (ValueTask)result;
    }

    private Type ResolveEventType(TinyOutboxMessage message)
    {
        if (eventTypes.TryGetValue(message.EventType, out var eventType))
        {
            return eventType;
        }

        throw new InvalidOperationException($"Event type '{message.EventType}' is not registered.");
    }

    private async ValueTask ProcessTypedMessageAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken)
    {
        var consumers = serviceProvider.GetServices<IEventConsumer<TEvent>>();

        foreach (var consumer in consumers)
        {
            await consumer.ConsumeAsync(@event, cancellationToken);
        }
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

    private static Dictionary<string, Type> BuildEventTypeMap(IEnumerable<TinyEventTypeDescriptor> descriptors)
    {
        var eventTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var descriptor in descriptors)
        {
            AddEventType(eventTypes, descriptor);
        }

        return eventTypes;
    }

    private static void AddEventType(
        Dictionary<string, Type> eventTypes,
        TinyEventTypeDescriptor descriptor)
    {
        if (eventTypes.TryGetValue(descriptor.EventTypeName, out var existingType))
        {
            if (existingType == descriptor.EventType)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Event type name '{descriptor.EventTypeName}' is registered for both '{existingType.FullName}' and '{descriptor.EventType.FullName}'.");
        }

        eventTypes.Add(descriptor.EventTypeName, descriptor.EventType);
    }
}
