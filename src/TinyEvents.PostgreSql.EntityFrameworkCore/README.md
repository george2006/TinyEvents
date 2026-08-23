# TinyEvents.PostgreSql.EntityFrameworkCore

PostgreSQL Entity Framework Core provider package for TinyEvents outbox storage and worker claiming.

This package is for applications that use a caller-owned `DbContext`. TinyEvents adds outbox messages to that context, and your application commits business data and outbox messages together with `SaveChangesAsync`.

The provider follows the same ownership model as the SQL Server EF Core provider:

- applications own the `DbContext`
- publishing adds an outbox message to the caller-owned `DbContext`
- TinyEvents does not call `SaveChangesAsync`
- business data and outbox messages commit together when the caller saves
- worker claiming uses PostgreSQL-specific SQL

PostgreSQL worker claiming uses `FOR UPDATE SKIP LOCKED`.

## Install

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.3
dotnet add package TinyEvents.PostgreSql.EntityFrameworkCore --version 0.1.0-alpha.3
```

## Register

```csharp
using Microsoft.EntityFrameworkCore;
using TinyEvents.PostgreSql.EntityFrameworkCore;

services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

services.UsePostgreSqlEntityFrameworkCoreOutbox<AppDbContext>();
```

Register exactly one TinyEvents database provider per service collection.

## Map The Outbox Entity

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseTinyEventsOutbox();
}
```

The default table is `TinyOutbox`.

## Custom Table Name

Configure both the provider and the EF Core mapping when using a custom table:

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

## Migrations

After building the host, explicitly apply the built-in PostgreSQL migrations before starting it:

```csharp
using TinyEvents;

var host = builder.Build();

await host.Services.MigrateTinyEventsAsync();
await host.RunAsync();
```

The migrator borrows the scoped `AppDbContext` connection, opens and closes it only when needed, and never disposes the context or its connection. The default history table is `public.TinyOutboxMigrations`; custom outbox names derive their history table in the same schema.

Processed-message cleanup is implemented for the next release and is not in the
latest published packages. That release adds migration
`002_AddProcessedCleanupIndex`; `MigrateTinyEventsAsync` applies it as a normal
forward-only migration when it is not already recorded.

TinyEvents never applies migrations automatically during service registration or worker startup.

## More Documentation

- Project documentation: https://github.com/george2006/TinyEvents
- EF Core provider guide: https://github.com/george2006/TinyEvents/blob/main/docs/postgresql/ef-core.md
- Worker guide: https://github.com/george2006/TinyEvents/blob/main/docs/workers.md
- Schema and migrations: https://github.com/george2006/TinyEvents/blob/main/docs/schema-and-migrations.md
- Retention and cleanup: https://github.com/george2006/TinyEvents/blob/main/docs/retention-and-cleanup.md
