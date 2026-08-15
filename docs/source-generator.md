# Source Generator

TinyEvents uses an incremental source generator to remove registration boilerplate.

Consumers are registered automatically through generated contributions. Application code defines `IEventConsumer<TEvent>` implementations; it does not normally add those consumers to DI by hand.

The generator discovers concrete closed event consumers:

```csharp
public sealed class SendWelcomeEmail : IEventConsumer<UserCreated>
{
    public ValueTask ConsumeAsync(
        UserCreated @event,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
```

## Generated Code

For each discovered consumer, the generator emits:

- DI registration for `IEventConsumer<TEvent>`
- event dispatcher registration for deserialization and consumer invocation
- a generated contribution
- a module initializer that adds the contribution to TinyEvents bootstrap

The generated registration is equivalent to:

```csharp
services.AddScoped<IEventConsumer<UserCreated>, SendWelcomeEmail>();
services.AddSingleton<ITinyEventDispatcher>(
    new TinyEventDispatcher<UserCreated>("MyApp.UserCreated"));
```

The generated code is packaged as an `ITinyEventsContribution`. A module initializer calls:

```csharp
TinyEventsBootstrap.AddContribution(new TinyEventsGeneratedContribution());
```

Provider registration calls TinyEvents core registration, and core registration applies all known contributions to the current `IServiceCollection`.

## No Runtime Scanning

TinyEvents does not scan assemblies at runtime to find consumers.

Runtime processing uses:

1. Generated `ITinyEventDispatcher` services to resolve the stored event type name.
2. `ITinyEventSerializer` to deserialize the payload.
3. Dependency injection to resolve and invoke `IEventConsumer<TEvent>` services.

Dependency injection is the consumer registry. `ITinyEventDispatcher` is the event-name-to-typed-dispatch map needed for deserialization and consumer invocation.

## Contribution Bootstrap

Generated contributions make multi-assembly projects work without runtime scanning.

Each assembly that contains consumers generates its own contribution. When that assembly is loaded, its module initializer adds the contribution to TinyEvents bootstrap. When the host calls a TinyEvents registration method, bootstrap applies the collected contributions once per service collection.

TinyEvents does not scan application assemblies or force-load referenced assemblies. If a consumer assembly has not been loaded before TinyEvents registration runs, that assembly's consumers and event dispatchers will not be registered in that service collection.

An outbox message whose stored event type has no registered dispatcher is treated as a processing failure by the worker. It follows the normal retry and max-attempt rules. Load consumer assemblies before TinyEvents registration so their generated contributions are available.

For example, register the one database provider used by the application:

```csharp
services.UseSqlServerEntityFrameworkCoreOutbox<AppDbContext>();
```

Provider registration methods register core services and apply generated contributions.

Calling core `UseTinyEvents(...)` registration more than once on the same service collection is safe. Database provider registration is intentionally exclusive: one service collection supports exactly one TinyEvents database provider.

## Generator Architecture

The generator uses Roslyn's `IIncrementalGenerator` API and follows the TinyValidations shape:

```text
Analysis
  -> Model
  -> Planning
  -> Emission
```

Analysis reads Roslyn syntax and symbols.

Model stores TinyEvents-owned facts.

Planning creates consumer registration and event dispatcher plans.

Emission writes generated C#.

Roslyn types should not leak beyond analysis except diagnostic reporting details.

## Diagnostics

Current diagnostics are intentionally small.

- `TEV001`: open generic event consumers are not supported.

Abstract consumers are ignored. Events do not require a marker interface.
