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
- event type descriptor registration for deserialization
- a generated contribution
- a module initializer that adds the contribution to TinyEvents bootstrap

The generated registration is equivalent to:

```csharp
services.AddScoped<IEventConsumer<UserCreated>, SendWelcomeEmail>();
services.AddSingleton(new TinyEventTypeDescriptor(
    "MyApp.UserCreated",
    typeof(UserCreated)));
```

The generated code is packaged as an `ITinyEventsContribution`. A module initializer calls:

```csharp
TinyEventsBootstrap.AddContribution(new TinyEventsGeneratedContribution());
```

Provider registration calls TinyEvents core registration, and core registration applies all known contributions to the current `IServiceCollection`.

## No Runtime Scanning

TinyEvents does not scan assemblies at runtime to find consumers.

Runtime processing uses:

1. Generated `TinyEventTypeDescriptor` services to resolve the stored event type name.
2. `ITinyEventSerializer` to deserialize the payload.
3. Dependency injection to resolve `IEnumerable<IEventConsumer<TEvent>>`.

Dependency injection is the consumer registry. `TinyEventTypeDescriptor` is only the event-name-to-CLR-type map needed for deserialization.

## Contribution Bootstrap

Generated contributions make multi-assembly projects work without runtime scanning.

Each assembly that contains consumers can contribute registrations. When the host calls a TinyEvents registration method, bootstrap applies the collected contributions once per service collection.

```csharp
services.UseSqlServerEntityFrameworkCoreOutbox<AppDbContext>();
```

or:

```csharp
services.UseSqlServerAdoNetOutbox(options =>
{
    // provider configuration
});
```

Both paths register core services and apply generated contributions.

Calling TinyEvents registration more than once on the same service collection is safe.

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

Planning creates consumer registration and event descriptor plans.

Emission writes generated C#.

Roslyn types should not leak beyond analysis except diagnostic reporting details.

## Diagnostics

Current diagnostics are intentionally small.

- `TEV001`: open generic event consumers are not supported.

Abstract consumers are ignored. Events do not require a marker interface.
