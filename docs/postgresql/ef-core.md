# PostgreSQL EF Core

`TinyEvents.PostgreSql.EntityFrameworkCore` stores outbox messages through a caller-owned `DbContext`.

## Install

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.3
dotnet add package TinyEvents.PostgreSql.EntityFrameworkCore --version 0.1.0-alpha.3
```

## Register

```csharp
using TinyEvents.PostgreSql.EntityFrameworkCore;

services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

services.UsePostgreSqlEntityFrameworkCoreOutbox<AppDbContext>();
```

Provider registration registers TinyEvents core services, applies generated consumer contributions, and configures the EF Core outbox writer/store.

## Map The Outbox Entity

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseTinyEventsOutbox();
}
```

The default table name is `TinyOutbox`.

## Custom Table Name

Configure both the provider and the model mapping:

```csharp
services.UsePostgreSqlEntityFrameworkCoreOutbox<AppDbContext>(options =>
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

The provider option controls SQL claiming and marking. The model builder extension controls EF model mapping.

The PostgreSQL mapping uses `text` for `EventType`, `Payload`, `ClaimedBy`, and `LastError`.

## Worker Startup Validation

The hosted worker validates that `TDbContext` uses the Npgsql EF Core provider
before polling. A missing or different EF Core provider stops startup because
the worker store executes PostgreSQL-specific claim and mark commands.

Validation reads EF Core provider metadata only. It does not open a database
connection, check credentials, inspect the schema, or run migrations. Connection
failures after startup remain operational iteration failures and follow the
worker's retry behavior.

## Worker Claiming

The PostgreSQL EF Core store opens the underlying relational connection when needed and executes PostgreSQL claim/mark statements.

Claiming is atomic and lease-based. PostgreSQL uses `FOR UPDATE SKIP LOCKED` inside an update/returning statement.

If the scoped `DbContext` has a current EF Core transaction, TinyEvents attaches claim and mark commands to that transaction. TinyEvents does not start, commit, or roll back that transaction.

The hosted worker creates a fresh scope for each processing iteration, so claim and mark commands normally run outside an application-owned transaction.

## Migrations

After building the host, explicitly run the built-in PostgreSQL migrations:

```csharp
using TinyEvents;

var host = builder.Build();

await host.Services.MigrateTinyEventsAsync();
await host.RunAsync();
```

The migrator borrows the scoped `DbContext` connection, opens and closes it only when needed, and never disposes the context or connection. The default history table is `public.TinyOutboxMigrations`; custom outbox tables derive their history table in the same schema.

TinyEvents does not migrate automatically during registration or worker startup.
