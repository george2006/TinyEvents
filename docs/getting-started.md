# Getting Started

This guide shows the basic TinyEvents flow with EF Core.

TinyEvents is outbox-first:

1. Your use case writes business data.
2. Your use case calls `ITinyEventPublisher.PublishAsync`.
3. TinyEvents stores an outbox message in the same persistence boundary.
4. A worker later claims and processes the message.

`PublishAsync` does not invoke consumers directly.

Think of consumers as domain-event or application-event handlers with outbox reliability. The outbox message is the durable record of the event to handle; your `IEventConsumer<TEvent>` is the handler. You keep the local handler model, but TinyEvents stores the work durably before a worker runs it.

## Install

Install the alpha packages:

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.3
dotnet add package TinyEvents.SqlServer.EntityFrameworkCore --version 0.1.0-alpha.3
dotnet add package TinyEvents.Worker --version 0.1.0-alpha.3
```

TinyEvents core is provider-agnostic. Provider packages are database-specific:

- SQL Server: `TinyEvents.SqlServer.EntityFrameworkCore` or `TinyEvents.SqlServer.AdoNet`
- PostgreSQL: `TinyEvents.PostgreSql.EntityFrameworkCore` or `TinyEvents.PostgreSql.AdoNet`

## Register Services

TinyEvents uses Microsoft dependency injection.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinyEvents;
using TinyEvents.SqlServer.EntityFrameworkCore;
using TinyEvents.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

builder.Services.UseSqlServerEntityFrameworkCoreOutbox<AppDbContext>();
builder.Services.AddTinyEventsWorker(options =>
{
    options.BatchSize = 50;
    options.PollingInterval = TimeSpan.FromSeconds(5);
    options.ClaimTimeout = TimeSpan.FromMinutes(5);
});
```

`UseSqlServerEntityFrameworkCoreOutbox<TDbContext>` registers TinyEvents core services, the EF Core outbox writer, and the EF Core outbox store.

It also applies generated TinyEvents contributions for assemblies already loaded in the process. Those contributions contain the consumer and event dispatcher registrations emitted by the source generator.

For PostgreSQL EF Core, replace the SQL Server provider package and registration method with:

```csharp
using TinyEvents.PostgreSql.EntityFrameworkCore;

builder.Services.UsePostgreSqlEntityFrameworkCoreOutbox<AppDbContext>();
```

Register exactly one TinyEvents database provider in an application.

## Map The Outbox

Call the model builder extension from your `DbContext`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseTinyEventsOutbox();
}
```

The default table name is `TinyOutbox`.

## Apply Built-In Migrations

Build the host, explicitly apply TinyEvents migrations, and then start it:

```csharp
var host = builder.Build();

await host.Services.MigrateTinyEventsAsync();
await host.RunAsync();
```

This ordering is the same for ASP.NET Core, worker-only, console, and test hosts.

Migration execution is never implicit. The hosted worker does not create or upgrade the schema during startup.

## Define An Event

TinyEvents does not require an event marker interface.

```csharp
public sealed record UserCreated(Guid UserId, string Email);
```

## Define A Consumer

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

The source generator discovers concrete `IEventConsumer<TEvent>` implementations and emits DI registrations automatically. You do not need to register normal event consumers by hand.

## Publish From A Use Case

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

With EF Core, the event becomes durable when `SaveChangesAsync` commits.

## Process The Outbox

For manual processing instead of the hosted worker, create a scope from the built host:

```csharp
await using var scope = host.Services.CreateAsyncScope();
var processor = scope.ServiceProvider.GetRequiredService<ITinyOutboxProcessor>();
await processor.ProcessPendingAsync(ct);
```

Processing resolves a generated `ITinyEventDispatcher` for the stored event type, deserializes the payload, and invokes matching `IEventConsumer<TEvent>` services through dependency injection.

Hosted processing was registered earlier with:

```csharp
using TinyEvents.Worker;

builder.Services.AddTinyEventsWorker(options =>
{
    options.BatchSize = 50;
    options.PollingInterval = TimeSpan.FromSeconds(5);
    options.ClaimTimeout = TimeSpan.FromMinutes(5);
});
```

Workers use lease-based claiming. If a worker crashes, claimed messages become claimable again after `ClaimTimeout`.

## Next

- [Run the Samples](../samples/README.md)
- [EF Core Providers](ef-core.md)
- [ADO.NET Providers](ado-net.md)
- [SQL Server EF Core](sql-server/ef-core.md)
- [PostgreSQL EF Core](postgresql/ef-core.md)
- [Workers and Leases](workers.md)
- [Schema and Migrations](schema-and-migrations.md)
- [The Tiny Suite](tiny-suite.md)
