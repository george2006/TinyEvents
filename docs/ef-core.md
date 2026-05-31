# EF Core Provider

`TinyEvents.SqlServer.EntityFrameworkCore` stores outbox messages through a caller-owned `DbContext`.

The current alpha uses SQL Server-specific claiming SQL for worker operations.

## Install

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.1
dotnet add package TinyEvents.SqlServer.EntityFrameworkCore --version 0.1.0-alpha.1
```

The EF Core provider package is SQL Server-specific.

## Register

```csharp
using TinyEvents.SqlServer.EntityFrameworkCore;

services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

services.UseSqlServerEntityFrameworkCoreOutbox<AppDbContext>();
```

This registers TinyEvents core services, applies generated consumer contributions, and configures the EF Core outbox writer/store.

## Map The Outbox Entity

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseTinyEventsOutbox();
}
```

The default table name is `TinyOutbox`.

## Custom Table Name

If you configure a custom table name, configure both the provider and the model mapping:

```csharp
services.UseSqlServerEntityFrameworkCoreOutbox<AppDbContext>(options =>
{
    options.TableName = "app.MyOutbox";
});
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseTinyEventsOutbox("app.MyOutbox");
}
```

The provider option controls SQL claiming and marking. The model builder extension controls EF mapping and migrations.

## Transaction Behavior

Publishing adds an outbox entity to the scoped `DbContext`. TinyEvents does not call `SaveChangesAsync`.

```csharp
dbContext.Users.Add(user);
await events.PublishAsync(new UserCreated(user.Id, user.Email), ct);
await dbContext.SaveChangesAsync(ct);
```

Business data and outbox messages commit together when the caller saves the context.

## Worker Processing

The EF Core store opens the underlying relational connection when needed and executes SQL Server claim/mark statements.

Claiming is atomic and lease-based:

- due `Pending` messages can be claimed
- expired `Processing` messages can be reclaimed
- active `Processing` messages are skipped
- processed/failed updates require the current worker id

## Migrations

Use normal EF Core migrations:

```bash
dotnet ef migrations add AddTinyEventsOutbox
dotnet ef database update
```

TinyEvents provides mapping. Your application owns migration generation and execution.
