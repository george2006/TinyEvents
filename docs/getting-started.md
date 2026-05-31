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
dotnet add package TinyEvents --version 0.1.0-alpha.1
dotnet add package TinyEvents.SqlServer.EntityFrameworkCore --version 0.1.0-alpha.1
```

TinyEvents core is provider-agnostic. The current alpha SQL Server providers ship as provider packages.

## Register Services

TinyEvents uses Microsoft dependency injection.

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

`UseSqlServerEntityFrameworkCoreOutbox<TDbContext>` registers TinyEvents core services, the EF Core outbox writer, and the EF Core outbox store.

It also applies generated TinyEvents contributions for the assemblies loaded in the process. Those contributions contain the consumer registrations and event type descriptors emitted by the source generator.

## Map The Outbox

Call the model builder extension from your `DbContext`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseTinyEventsOutbox();
}
```

The default table name is `TinyOutbox`.

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

For manual processing:

```csharp
var processor = provider.GetRequiredService<ITinyOutboxProcessor>();
await processor.ProcessPendingAsync(ct);
```

Processing resolves generated `TinyEventTypeDescriptor` services to deserialize the payload, then resolves `IEnumerable<IEventConsumer<TEvent>>` from dependency injection.

For hosted processing:

```csharp
using TinyEvents.Worker;

services.AddTinyEventsWorker(options =>
{
    options.BatchSize = 50;
    options.PollingInterval = TimeSpan.FromSeconds(5);
    options.ClaimTimeout = TimeSpan.FromMinutes(5);
});
```

Workers use lease-based claiming. If a worker crashes, claimed messages become claimable again after `ClaimTimeout`.

## Next

- [EF Core Provider](ef-core.md)
- [ADO.NET Provider](ado-net.md)
- [Workers and Leases](workers.md)
- [Schema and Migrations](schema-and-migrations.md)
- [The Tiny Suite](tiny-suite.md)
