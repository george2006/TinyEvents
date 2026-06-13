# PostgreSQL EF Core

`TinyEvents.PostgreSql.EntityFrameworkCore` stores outbox messages through a caller-owned `DbContext`.

## Install

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.2
dotnet add package TinyEvents.PostgreSql.EntityFrameworkCore --version 0.1.0-alpha.2
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

The provider option controls SQL claiming and marking. The model builder extension controls EF mapping and migrations.

## Worker Claiming

The PostgreSQL EF Core store opens the underlying relational connection when needed and executes PostgreSQL claim/mark statements.

Claiming is atomic and lease-based. PostgreSQL uses `FOR UPDATE SKIP LOCKED` inside an update/returning statement.

## Migrations

Use normal EF Core migrations:

```bash
dotnet ef migrations add AddTinyEventsOutbox
dotnet ef database update
```

TinyEvents provides mapping. Your application owns migration generation and execution.
