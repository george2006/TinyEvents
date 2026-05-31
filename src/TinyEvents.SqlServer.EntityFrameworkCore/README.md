# TinyEvents.SqlServer.EntityFrameworkCore

SQL Server Entity Framework Core provider for TinyEvents outbox storage and worker claiming.

This package is for applications that use a caller-owned `DbContext`. TinyEvents adds outbox messages to that context, and your application commits business data and outbox messages together with `SaveChangesAsync`.

## Install

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.1
dotnet add package TinyEvents.SqlServer.EntityFrameworkCore --version 0.1.0-alpha.1
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

The provider option controls SQL claiming and marking. The model builder extension controls EF mapping and migrations.

## More Documentation

- EF Core provider guide: https://github.com/george2006/TinyEvents/blob/main/docs/ef-core.md
- Schema and migrations: https://github.com/george2006/TinyEvents/blob/main/docs/schema-and-migrations.md
- Worker guide: https://github.com/george2006/TinyEvents/blob/main/docs/workers.md
