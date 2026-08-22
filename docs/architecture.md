# Architecture

TinyEvents has five main parts:

- core runtime
- incremental source generator
- providers
- worker integration
- built-in migrations

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
- Generated contributions register consumers and event dispatchers with dependency injection.
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

Applications execute provider migrations through:

```csharp
await host.Services.MigrateTinyEventsAsync(cancellationToken);
```

`ITinyEventsMigrator` is the scoped provider contract behind that host-level entry point.

## Runtime Project

```text
src/TinyEvents
  Abstractions
  DependencyInjection
  Generation
  Migrations
  Options
  Outbox
  Processing
  Publishing
  Registry
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
```

Provider projects register implementations of core abstractions. Providers do not implement publisher or processor behavior.

Provider registration also calls core registration, which applies generated TinyEvents contributions to the service collection.

One service collection supports exactly one TinyEvents database provider. Repeated core registration is safe, but a second provider registration fails immediately with both provider identities.

Current database providers:

- SQL Server ADO.NET provider
- SQL Server EF Core provider
- PostgreSQL ADO.NET provider
- PostgreSQL EF Core provider

## Worker Project

```text
src/TinyEvents.Worker
```

Worker integration validates the processing graph before polling, creates a dependency-injection scope per iteration, and keeps polling after non-cancellation operational failures.

## Migration Architecture

Common planning, history models, checksums, and structured logging compile into each database provider assembly from shared source. Database-specific history, locking, SQL, and orchestration compile into the matching SQL Server or PostgreSQL provider assembly.

There is no migration package or migration DLL. ADO.NET adapters create dedicated connections through the worker connection factory. EF Core adapters borrow the scoped `DbContext` connection without changing its ownership.

Migration execution is explicit and forward-only:

```text
host service provider
  -> asynchronous migration scope
  -> scoped provider migrator
  -> provider history and session lock
  -> ordered pending migrations
  -> final history verification
```

## Publishing Flow

1. Reject a null event.
2. Resolve the event type name.
3. Serialize the payload with `System.Text.Json`.
4. Create a pending outbox message.
5. Add the message through the configured provider writer.
6. Return without invoking consumers.

EF Core publishing adds the message to the current `DbContext`.

ADO.NET publishing inserts the message through the current application transaction.

EF Core worker claim and mark commands use the scoped `DbContext` relational connection and attach to `DbContext.Database.CurrentTransaction` when one exists. TinyEvents does not create or complete EF Core transactions for worker operations.

## Processing Flow

1. Resolve the current worker id.
2. Claim pending or expired processing messages.
3. Resolve the event dispatcher for the stored event type.
4. Deserialize the payload.
5. Resolve all `IEventConsumer<TEvent>` instances from DI.
6. Invoke consumers through the generated dispatcher.
7. Mark processed if all consumers succeed.
8. Mark failed or scheduled for retry when a consumer fails.

One outbox message represents one event, not one message per consumer. If one consumer fails, the event message is retried.

If marking a message as processed or failed no longer updates a row because the worker lost ownership of the processing lease, the processor leaves the message alone and continues. Lease loss is not recorded as a consumer failure.

Failures that are recorded through `MarkFailedAsync` advance the message attempt count. Before `MaxAttempts` is reached, the message is made pending again and delayed until `NextAttemptAtUtc`. When `MaxAttempts` is reached, the message is marked failed and no next attempt is scheduled.

Unknown event types are processing failures because the processor cannot resolve a generated dispatcher for the stored event type.

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

Provider completion and failure updates must validate affected row counts. A mark operation that updates no rows means the worker no longer owns a processing lease for that message.

Processed-message retention uses a separate storage contract because cleanup is
not part of claiming or lease ownership:

```csharp
public interface ITinyOutboxCleanupStore
{
    ValueTask<int> DeleteProcessedBeforeAsync(
        DateTimeOffset cutoffUtc,
        int maxCount,
        CancellationToken cancellationToken);
}
```

The command must delete at most `maxCount` messages, must use the exclusive
`ProcessedAtUtc < cutoffUtc` boundary, and must never delete another status.
Provider implementations make selection and deletion one atomic database
statement so independent cleanup workers can run concurrently.

New database providers must implement atomic claiming safely for their database engine. Query-then-update is not acceptable for multi-worker processing.

## Bootstrap

Generated assemblies add contributions through module initializers.

`TinyEventsBootstrap.Apply(IServiceCollection)` applies contributions once per service collection.

The contribution system is the bridge between compile-time discovery and runtime DI registration:

1. The generator emits an `ITinyEventsContribution`.
2. A module initializer adds it to `TinyEventsBootstrap`.
3. `UseTinyEvents` or a provider registration method applies contributions.
4. Consumers and event dispatchers become normal DI services.

TinyEvents does not scan assemblies or load consumer assemblies at runtime. Contributions are available only after the assembly that contains them has been loaded and its module initializer has run. Call TinyEvents registration after consumer assemblies are loaded.

Runtime processing does not use a custom consumer registry. It resolves consumers directly from `IServiceProvider` through generated dispatchers.
