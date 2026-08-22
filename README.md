# TinyEvents

TinyEvents is a small outbox-first application-event library for .NET.

It helps application code publish durable events without turning `PublishAsync` into inline consumer dispatch. Events are stored in an outbox first. Workers claim and process them later.

In practice, TinyEvents gives you domain-event or application-event handlers with the reliability guarantees of the outbox pattern. The outbox message is the durable record of the event to handle; your `IEventConsumer<TEvent>` remains the handler. You get reliable event handling without requiring a message bus, broker, or separate async messaging platform.

> Status: `0.1.0-alpha.3` / active development.
>
> TinyEvents is not production-ready yet. Development is ongoing, the API surface may change before 1.0, and the current release is meant for experimentation, feedback, and integration work.
>
> `0.1.0-alpha.3` is the feature-complete final alpha. The next phase focuses on contract stabilization and production-readiness evidence before 1.0. Publishing to NuGet at this stage does not mean the library is stable for production systems.

## Public Reliability Laboratory

Want to see how TinyEvents is being hardened? The public [TinyEvents Dogfood laboratory](https://github.com/george2006/TinyEvents.DogFood) exercises worker crashes, competing workers, leases, database outages, retries, schema changes, and load against real infrastructure.

The laboratory is intentionally a work in progress. Completed scenarios contain reproducible evidence; its public roadmap shows what still has to be proven before beta. Pending work is not presented as a product guarantee.

## Contents

- [Why TinyEvents?](#why-tinyevents)
- [Quick start](#quick-start)
- [Publishing and consuming](#publishing-and-consuming)
- [Providers](#providers)
- [Workers and leases](#workers-and-leases)
- [Retention and cleanup](#retention-and-cleanup)
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
- Incremental source generation registers consumers and event dispatchers automatically.
- Runtime dispatch resolves consumers from Microsoft dependency injection.
- Worker claiming is database-backed and lease-based.
- Delivery is at-least-once, not exactly-once.

The goal is not to be a broker abstraction. The goal is reliable event handling inside applications that already use a database: keep the developer model close to domain-event handlers, but make delivery durable through the outbox.

That means TinyEvents sits in the space between plain in-process event handlers and external messaging. It keeps the local handler model, but gives the handler a durable retryable execution path.

## Quick start

Install the alpha packages:

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.3
dotnet add package TinyEvents.SqlServer.EntityFrameworkCore --version 0.1.0-alpha.3
dotnet add package TinyEvents.Worker --version 0.1.0-alpha.3
```

Provider packages are database-specific. Use `TinyEvents.SqlServer.*` for SQL Server or `TinyEvents.PostgreSql.*` for PostgreSQL.

Register TinyEvents and the SQL Server EF Core provider:

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
- event dispatchers for deserialization and consumer invocation
- a module-initialized contribution to TinyEvents bootstrap

No runtime assembly scanning is required, and normal consumers do not need manual DI registration.

## Providers

TinyEvents core is provider-agnostic. The current alpha includes SQL Server and PostgreSQL provider packages:

- `TinyEvents.SqlServer.EntityFrameworkCore`
- `TinyEvents.SqlServer.AdoNet`
- `TinyEvents.PostgreSql.EntityFrameworkCore`
- `TinyEvents.PostgreSql.AdoNet`
- `TinyEvents.Worker`

Each database family has its own provider package. SQL Server providers use SQL Server locking hints and atomic update/output statements. PostgreSQL providers use PostgreSQL claiming semantics, including `FOR UPDATE SKIP LOCKED` with update/returning statements.

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

## Retention and cleanup

> **Release status:** This capability is implemented for the next TinyEvents
> release. It is not included in the latest published NuGet packages yet.

The hosted worker removes processed outbox messages after one hour by default,
using bounded provider-specific delete batches. Pending, processing, and failed
messages are never removed automatically in v1.

Cleanup is configurable and runs independently from event processing. See
[Retention and Cleanup](docs/retention-and-cleanup.md) for exact eligibility,
concurrency behavior, migration requirements, and storage-budget guidance.

## Schema and migrations

TinyEvents owns forward-only migrations for its outbox schema. Execution is explicit: register exactly one database provider, build the host, migrate, and then run it.

```csharp
var host = builder.Build();

await host.Services.MigrateTinyEventsAsync();
await host.RunAsync();
```

The same entry point works with ASP.NET Core, worker-only hosts, console applications, and test hosts. It creates its own dependency-injection scope and applies only migrations that are absent from the provider-specific history table.

TinyEvents never migrates automatically during service registration or worker startup. Applications choose when migration execution is safe.

The default outbox tables are `dbo.TinyOutbox` on SQL Server and `public.TinyOutbox` on PostgreSQL. Their history tables are `dbo.TinyOutboxMigrations` and `public.TinyOutboxMigrations`. Custom outbox names derive the history name in the same schema.

Existing alpha databases receive one narrow compatibility behavior: when the history table is absent and the configured outbox table already exists, the initial migration is recorded as a baseline without recreating the table. TinyEvents does not inspect or repair that table, so manually altered or incompatible alpha schemas must be reconciled by the application first.

See [Schema and Migrations](docs/schema-and-migrations.md) for provider ownership details, logging, and upgrade guidance.

## Run the samples

Start the sample database containers with Docker:

```bash
docker compose up -d sqlserver postgresql
```

Then run the SQL Server EF Core sample:

```bash
dotnet run --project samples/TinyEvents.Sample.EfCore
```

Or run the ADO.NET sample:

```bash
dotnet run --project samples/TinyEvents.Sample.AdoNet
```

Or run the PostgreSQL EF Core sample:

```bash
dotnet run --project samples/TinyEvents.Sample.PostgreSql.EfCore
```

Or run the PostgreSQL ADO.NET sample:

```bash
dotnet run --project samples/TinyEvents.Sample.PostgreSql.AdoNet
```

SQL Server samples default to `TINYEVENTS_SAMPLE_SQLSERVER`. PostgreSQL samples default to `TINYEVENTS_SAMPLE_POSTGRESQL`. All samples also accept a command-line connection string. See [Samples](samples/README.md) for the full runbook and the package smoke sample.

## Tiny suite

TinyEvents belongs to the Tiny suite:

| Project | Kind | Responsibility |
| --- | --- | --- |
| [TinyDispatcher](https://github.com/george2006/TinyDispatcher) | Library | Command and query execution |
| [TinyValidations](https://github.com/george2006/TinyValidations) | Library | Application input validation |
| [TinyEvents](https://github.com/george2006/TinyEvents) | Library | Reliable application-event handling through the outbox pattern |
| [TheTinyApplicationLayer](https://github.com/george2006/TheTinyApplicationLayer) | Example | Runnable ASP.NET Core and Blazor application using the complete suite |

The three libraries can be adopted independently. Using one does not require referencing the other two.

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
- Source generation for consumer registration and event dispatchers.
- Contribution-based bootstrap for generated registrations.
- Provider isolation.
- Database-backed lease claiming.
- At-least-once delivery.
- Idempotent consumers.
- Small public contracts before convenience APIs.

## Documentation

- [Getting Started](docs/getting-started.md)
- [EF Core Providers](docs/ef-core.md)
- [ADO.NET Providers](docs/ado-net.md)
- [SQL Server EF Core](docs/sql-server/ef-core.md)
- [SQL Server ADO.NET](docs/sql-server/ado-net.md)
- [PostgreSQL EF Core](docs/postgresql/ef-core.md)
- [PostgreSQL ADO.NET](docs/postgresql/ado-net.md)
- [Workers and Leases](docs/workers.md)
- [Schema and Migrations](docs/schema-and-migrations.md)
- [Upgrading to 0.1.0-alpha.3](docs/upgrading-to-alpha-3.md)
- [The Tiny Suite](docs/tiny-suite.md)
- [Source Generator](docs/source-generator.md)
- [Event Contracts and Durable Names](docs/event-contracts.md)
- [Architecture](docs/architecture.md)
- [Testing](docs/testing.md)
- [Samples](samples/README.md)
- [Roadmap](docs/roadmap.md)

## Current limitations

TinyEvents is an alpha.

- SQL Server and PostgreSQL are the current real database targets.
- Providers use database-specific atomic claiming.
- There is no claim heartbeat or renewal in v1.
- Long-running consumers must use a long enough `ClaimTimeout`.
- Built-in migrations are forward-only; down migrations and schema repair are not provided.
- Exactly-once side effects are not guaranteed.
- Native ASP.NET convenience integration is intentionally not the first layer; the samples use minimal APIs directly.

See [Roadmap](docs/roadmap.md) for planned hardening.

## Why publish an alpha?

TinyEvents is published as a real package train so package boundaries, database behavior, and upgrade compatibility are exercised outside project-reference builds.

The final alpha validates:

- package boundaries
- generated registrations
- SQL Server and PostgreSQL provider behavior
- worker processing
- sample application ergonomics
- how TinyDispatcher, TinyValidations, and TinyEvents fit together as an application layer

Use the alpha to evaluate the design, exercise upgrades, and give feedback. The beta phase will focus on stabilizing public, package, database, and operational contracts before 1.0.
