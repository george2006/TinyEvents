# TinyEvents.SqlServer.EntityFrameworkCore

SQL Server Entity Framework Core provider for TinyEvents outbox storage and worker claiming.

This package is for applications that use a caller-owned `DbContext`. TinyEvents adds outbox messages to that context, and your application commits business data and outbox messages together with `SaveChangesAsync`.

## Install

```bash
dotnet add package TinyEvents --version 0.1.0-beta.1
dotnet add package TinyEvents.SqlServer.EntityFrameworkCore --version 0.1.0-beta.1
```

## Register

```csharp
using Microsoft.EntityFrameworkCore;
using TinyEvents.SqlServer.EntityFrameworkCore;

services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

services.UseSqlServerEntityFrameworkCoreOutbox<AppDbContext>();
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

The provider option controls SQL claiming and marking. The model builder extension controls EF model mapping.

## Migrations

After building the host, explicitly apply the built-in SQL Server migrations before starting it:

```csharp
using TinyEvents;

var host = builder.Build();

await host.Services.MigrateTinyEventsAsync();
await host.RunAsync();
```

The migrator borrows the scoped `AppDbContext` connection, opens and closes it only when needed, and never disposes the context or its connection. The default history table is `dbo.TinyOutboxMigrations`; custom outbox names derive their history table in the same schema.

Processed-message cleanup is included in `0.1.0-beta.1`. Migration
`002_AddProcessedCleanupIndex` adds its ordered lookup;
`MigrateTinyEventsAsync` applies it as a normal forward-only migration when it
is not already recorded.

TinyEvents never applies migrations automatically during service registration or worker startup.

## More Documentation

- EF Core provider guide: https://github.com/george2006/TinyEvents/blob/main/docs/sql-server/ef-core.md
- Schema and migrations: https://github.com/george2006/TinyEvents/blob/main/docs/schema-and-migrations.md
- Worker guide: https://github.com/george2006/TinyEvents/blob/main/docs/workers.md
- Retention and cleanup: https://github.com/george2006/TinyEvents/blob/main/docs/retention-and-cleanup.md
