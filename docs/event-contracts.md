# Event Contracts and Durable Names

TinyEvents stores an event name beside every serialized outbox payload. The name is part of the durable contract: a worker must resolve it before it can deserialize and dispatch the event.

## Default Event Name

By default, TinyEvents uses the runtime `Type.FullName` for both publishing and generated dispatch. The publisher and dispatcher share the same naming implementation.

This supports:

- top-level event classes and records;
- nested event classes and records;
- moving a contract between assemblies while preserving its full type name.

Generic event contracts are deliberately unsupported. The source generator reports error `TEV002` for a closed construction such as `IEventConsumer<GenericEvent<int>>`. Use a dedicated non-generic event type instead.

## Namespace or Type Renames

Renaming a namespace or event type changes `Type.FullName`. Messages already stored in the outbox retain the previous name.

TinyEvents cannot safely infer whether a new type replaces an old contract or represents a different business event. Applications must state that relationship explicitly:

```csharp
services.UseTinyEvents(options =>
{
    options.AcceptPreviousEventName<OrderCreated>(
        "Old.Namespace.OrderCreated");
});
```

The configuration lambda is ordinary runtime configuration. The source generator does not analyze it. The generator discovers `IEventConsumer<TEvent>` implementations; the processor then validates configured previous names against those generated dispatchers.

When a message contains `Old.Namespace.OrderCreated`, TinyEvents:

1. resolves the generated dispatcher for the current `OrderCreated` type;
2. deserializes the stored payload into the current type;
3. invokes all current `IEventConsumer<OrderCreated>` consumers.

## Validation

TinyEvents rejects invalid mappings when the processor graph is created:

- an empty previous name;
- a previous name targeting an event without a generated dispatcher;
- one name resolving to two different event types;
- a previous name hiding another event's current name.

Registering the same previous name for the same event more than once is harmless.

## Deployment Guidance

Add the previous name before deploying the renamed contract. Keep it while messages using that name can remain pending or eligible for retry.

The mapping does not automatically requeue messages that already reached the terminal `Failed` state. Operational recovery of terminal failures is a separate concern.

If no explicit previous name is configured, a namespace or type rename is a breaking change for messages already stored in the outbox. Drain those messages before deploying the rename.
