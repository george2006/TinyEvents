# TinyEvents

TinyEvents is a small outbox-first application-event library for .NET.

It helps application code publish durable events without turning `PublishAsync` into inline consumer dispatch. Events are stored in an outbox first. Workers claim and process them later.

In practice, TinyEvents gives you domain-event or application-event handlers with the reliability guarantees of the outbox pattern. The outbox message is the durable record of the event to handle; your `IEventConsumer<TEvent>` remains the handler. You get reliable event handling without requiring a message bus, broker, or separate async messaging platform.

> Status: early alpha / active development.
>
> TinyEvents is not production-ready yet. Development is ongoing, the API surface may change before 1.0, and the current release is meant for experimentation, feedback, and integration work.
>
> Alpha packages are published so we can build and test a complete Tiny Suite sample with TinyDispatcher, TinyValidations, and TinyEvents using real package references. Publishing to NuGet at this stage does not mean the library is stable for production systems.

## Contents

- [Why TinyEvents?](#why-tinyevents)
- [Quick start](#quick-start)
- [Publishing and consuming](#publishing-and-consuming)
- [Providers](#providers)
- [Workers and leases](#workers-and-leases)
- [Schema and migrations](#schema-and-migrations)
- [Run the samples](#run-the-samples)
- [Tiny suite](#tiny-suite)
- [Design principles](#design-principles)
- [Documentation](#documentation)
- [Current limitations](#current-limitations)
- [Why publish an alpha?](#why-publish-an-alpha)

## Why TinyEvents?

Most application-event APIs make publishing look like direct notification.

TinyEvents is deliberately different:

- `PublishAsync` stores an outbox message.
- Consumers run later through the processor or hosted worker.
- Incremental source generation registers consumers and event type descriptors automatically.
- Runtime dispatch resolves consumers from Microsoft dependency injection.
- Worker claiming is database-backed and lease-based.
- Delivery is at-least-once, not exactly-once.

The goal is not to be a broker abstraction. The goal is reliable event handling inside applications that already use a database: keep the developer model close to domain-event handlers, but make delivery durable through the outbox.

That means TinyEvents sits in the space between plain in-process event handlers and external messaging. It keeps the local handler model, but gives the handler a durable retryable execution path.

## Quick start

Install the alpha packages:

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.1
dotnet add package TinyEvents.SqlServer.EntityFrameworkCore --version 0.1.0-alpha.1
```

Register TinyEvents and the EF Core provider:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TinyEvents;
using TinyEvents.SqlServer.EntityFrameworkCore;

var services = new ServiceCollection();

services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

services.UseSqlServerEntityFrameworkCoreOutbox<AppDbContext>();
```

Provider registration also applies generated TinyEvents contributions. If a referenced assembly contains concrete `IEventConsumer<TEvent>` implementations, the generator contributes the consumer registrations automatically.

Map the outbox entity in your `DbContext`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseTinyEventsOutbox();
}
```

Publish inside your use case:

```csharp
public sealed class RegisterUserUseCase
{
    private readonly AppDbContext dbContext;
    private readonly ITinyEventPublisher events;

    public RegisterUserUseCase(
        AppDbContext dbContext,
        ITinyEventPublisher events)
    {
        this.dbContext = dbContext;
        this.events = events;
    }

    public async ValueTask<Guid> RegisterAsync(string email, CancellationToken ct)
    {
        var userId = Guid.NewGuid();

        dbContext.Users.Add(new UserRow { Id = userId, Email = email });
        await events.PublishAsync(new UserCreated(userId, email), ct);
        await dbContext.SaveChangesAsync(ct);

        return userId;
    }
}
```

## Publishing and consuming

Events are plain records or classes:

```csharp
public sealed record UserCreated(Guid UserId, string Email);
```

Consumers implement `IEventConsumer<TEvent>`:

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

The source generator discovers concrete closed consumers and emits:

- `IEventConsumer<TEvent>` DI registrations
- event type descriptors for deserialization
- a module-initialized contribution to TinyEvents bootstrap

No runtime assembly scanning is required, and normal consumers do not need manual DI registration.

## Providers

TinyEvents core is provider-agnostic. The current alpha includes SQL Server provider packages:

- `TinyEvents.SqlServer.EntityFrameworkCore`
- `TinyEvents.SqlServer.AdoNet`
- `TinyEvents.Worker`

Other databases may be supported later through separate providers.

The SQL Server providers use SQL Server-specific claiming semantics, including SQL Server locking hints and atomic claim statements.

EF Core publishing adds the outbox message to the caller's scoped `DbContext`. The caller commits business data and outbox messages with `SaveChangesAsync`.

ADO.NET publishing requires an application-owned `DbConnection` and `DbTransaction`. TinyEvents joins that transaction and never starts, commits, rolls back, or disposes it.

## Workers and leases

Workers claim outbox rows with database-backed leases.

When a worker claims a message:

- `ClaimedBy` is set to the worker id
- `ClaimedAtUtc` is set to the claim time
- `ClaimExpiresAtUtc` is set to claim time plus `ClaimTimeout`

If a worker crashes, no cleanup is required. The message remains `Processing` until the claim expires. Another worker can reclaim it after expiration.

Consumers must be idempotent. TinyEvents guarantees at-least-once delivery, not exactly-once side effects.

## Schema and migrations

TinyEvents owns the outbox schema definition. Applications own migration execution.

EF Core applications should call `modelBuilder.UseTinyEventsOutbox()` and create normal EF migrations.

ADO.NET applications should use the provided SQL Server script:

```csharp
var sql = TinySqlServerAdoNetSchema.CreateOutboxSql();
```

The package also includes the default SQL script as package content:

```text
schema/sqlserver/001_CreateTinyOutbox.sql
```

TinyEvents does not run migrations automatically.

## Run the samples

First start SQL Server with Docker:

```bash
docker compose up -d sqlserver
```

Then run the EF Core sample:

```bash
dotnet run --project samples/TinyEvents.Sample.EfCore
```

Or run the ADO.NET sample:

```bash
dotnet run --project samples/TinyEvents.Sample.AdoNet
```

The samples default to `TINYEVENTS_SAMPLE_SQLSERVER` or a command-line connection string. See [Samples](samples/README.md) for the full runbook and the package smoke sample.

## Tiny suite

TinyEvents is part of the same thinking as TinyDispatcher and TinyValidations.

Together they can form a small application layer:

```text
command/query
  -> validation
  -> dispatch
  -> use case
  -> durable event publication
  -> event consumer
```

The suite is intentionally small: explicit contracts, generated mechanical code, no runtime scanning, and boring runtime behavior.

Read more in [The Tiny Suite](docs/tiny-suite.md).

## Design principles

TinyEvents is intentionally small.

- Outbox-first publishing.
- No direct consumer invocation from `PublishAsync`.
- Domain/application event handling with outbox reliability.
- Plain event objects, no marker interface.
- No runtime scanning.
- Source generation for consumer registration and event type descriptors.
- Contribution-based bootstrap for generated registrations.
- Provider isolation.
- Database-backed lease claiming.
- At-least-once delivery.
- Idempotent consumers.
- Small public contracts before convenience APIs.

## Documentation

- [Getting Started](docs/getting-started.md)
- [EF Core Provider](docs/ef-core.md)
- [ADO.NET Provider](docs/ado-net.md)
- [Workers and Leases](docs/workers.md)
- [Schema and Migrations](docs/schema-and-migrations.md)
- [The Tiny Suite](docs/tiny-suite.md)
- [Source Generator](docs/source-generator.md)
- [Architecture](docs/architecture.md)
- [Testing](docs/testing.md)
- [Samples](samples/README.md)
- [Roadmap](docs/roadmap.md)

## Current limitations

TinyEvents is an alpha.

- SQL Server is the only real database target in the current providers.
- SQL Server providers use SQL Server-specific atomic claiming.
- There is no claim heartbeat or renewal in v1.
- Long-running consumers must use a long enough `ClaimTimeout`.
- There is no migration runner.
- Exactly-once side effects are not guaranteed.
- Native ASP.NET convenience integration is intentionally not the first layer; the samples use minimal APIs directly.

See [Roadmap](docs/roadmap.md) for planned hardening.

## Why publish an alpha?

TinyEvents is being published early because the Tiny Suite needs to be exercised as real packages, not only as local project references.

The goal of the alpha is to validate:

- package boundaries
- generated registrations
- SQL Server provider behavior
- worker processing
- sample application ergonomics
- how TinyDispatcher, TinyValidations, and TinyEvents fit together as an application layer

Use the alpha to explore the design, build samples, and give feedback. Wait for later releases before treating the public API as stable.
