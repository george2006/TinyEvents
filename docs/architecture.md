# Architecture

TinyEvents has four main parts:

- core runtime
- incremental source generator
- providers
- worker integration

The core library is host-agnostic and provider-agnostic.

TinyEvents is meant for domain-event and application-event handling when those handlers need outbox reliability. The outbox message is the durable record of an event that still needs handling; the consumer remains the handler. You do not need to introduce a broker, bus abstraction, or separate async messaging platform for this class of side effect.

## Design Principles

TinyEvents is designed to stay small enough to reason about.

- Events are durable work, not synchronous callbacks.
- `PublishAsync` means accepted for reliable delivery.
- Consumers run outside the original use-case path.
- Event handlers stay local while delivery is protected by the outbox.
- User events are plain records or classes.
- Core stays host-agnostic.
- Providers are isolated in separate class libraries.
- No runtime assembly scanning.
- Source generation removes registration and event-map boilerplate.
- Generated contributions register consumers with dependency injection.
- Delivery is at-least-once, not exactly-once.
- Database-backed claim leases support multiple workers.
- Consumers should be idempotent.

## Public Contracts

Publishing code depends on:

```csharp
public interface ITinyEventPublisher
{
    ValueTask PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default);
}
```

Consumer code depends on:

```csharp
public interface IEventConsumer<TEvent>
{
    ValueTask ConsumeAsync(
        TEvent @event,
        CancellationToken cancellationToken);
}
```

## Runtime Project

```text
src/TinyEvents
  Abstractions
  DependencyInjection
  Generation
  Options
  Outbox
  Processing
  Publishing
  Serialization
```

## Source Generator Project

```text
src/TinyEvents.SourceGen
  Analysis
  Model
  Planning
  Emission
  Generation
  Validation
```

## Provider Projects

```text
src/TinyEvents.SqlServer.EntityFrameworkCore
src/TinyEvents.SqlServer.AdoNet
src/TinyEvents.PostgreSql.EntityFrameworkCore
src/TinyEvents.PostgreSql.AdoNet
src/TinyEvents.Worker
```

Provider projects register implementations of core abstractions. Providers do not implement publisher or processor behavior.

Provider registration also calls core registration, which applies generated TinyEvents contributions to the service collection.

Current database providers:

- SQL Server ADO.NET provider
- SQL Server EF Core provider
- PostgreSQL ADO.NET provider
- PostgreSQL EF Core provider

Future database providers may target MySQL, SQLite, or other engines if they can implement safe atomic claiming for that database.

## Publishing Flow

1. Reject a null event.
2. Resolve the event type name.
3. Serialize the payload with `System.Text.Json`.
4. Create a pending outbox message.
5. Add the message through the configured provider writer.
6. Return without invoking consumers.

EF Core publishing adds the message to the current `DbContext`.

ADO.NET publishing inserts the message through the current application transaction.

## Processing Flow

1. Resolve the current worker id.
2. Claim pending or expired processing messages.
3. Resolve event type through generated event descriptors.
4. Deserialize the payload.
5. Resolve all `IEventConsumer<TEvent>` instances from DI.
6. Invoke consumers.
7. Mark processed if all consumers succeed.
8. Mark failed or scheduled for retry when a consumer fails.

One outbox message represents one event, not one message per consumer. If one consumer fails, the event message is retried.

## Claiming Contract

```csharp
public interface ITinyOutboxWriter
{
    ValueTask AddAsync(
        TinyOutboxMessage message,
        CancellationToken cancellationToken);
}

public interface ITinyOutboxStore
{
    ValueTask<IReadOnlyList<TinyOutboxMessage>> ClaimPendingAsync(
        int maxCount,
        string workerId,
        DateTimeOffset now,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken);

    ValueTask MarkProcessedAsync(
        Guid messageId,
        string workerId,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken);

    ValueTask MarkFailedAsync(
        Guid messageId,
        string workerId,
        string error,
        int attemptCount,
        DateTimeOffset? nextAttemptAtUtc,
        CancellationToken cancellationToken);
}
```

Provider claiming must be atomic. Query-then-update claiming is not acceptable for DB providers.

New database providers must implement atomic claiming safely for their database engine. Query-then-update is not acceptable for multi-worker processing.

## Bootstrap

Generated assemblies add contributions through module initializers.

`TinyEventsBootstrap.Apply(IServiceCollection)` applies contributions once per service collection.

The contribution system is the bridge between compile-time discovery and runtime DI registration:

1. The generator emits an `ITinyEventsContribution`.
2. A module initializer adds it to `TinyEventsBootstrap`.
3. `UseTinyEvents` or a provider registration method applies contributions.
4. Consumers and event type descriptors become normal DI services.

Runtime processing does not use a custom consumer registry. It resolves consumers directly from `IServiceProvider`.
