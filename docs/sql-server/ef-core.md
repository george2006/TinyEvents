# SQL Server EF Core

`TinyEvents.SqlServer.EntityFrameworkCore` stores outbox messages through a caller-owned `DbContext`.

## Install

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.2
dotnet add package TinyEvents.SqlServer.EntityFrameworkCore --version 0.1.0-alpha.2
```

## Register

```csharp
using TinyEvents.SqlServer.EntityFrameworkCore;

services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

services.UseSqlServerEntityFrameworkCoreOutbox<AppDbContext>();
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

The SQL Server mapping uses `NVARCHAR(512)` for `EventType`, `NVARCHAR(MAX)` for `Payload`, `NVARCHAR(256)` for `ClaimedBy`, and `NVARCHAR(MAX)` for `LastError`.

## Worker Startup Validation

The hosted worker validates that `TDbContext` uses the SQL Server EF Core
provider before polling. A missing or different EF Core provider stops startup
because the worker store executes SQL Server-specific claim and mark commands.

Validation reads EF Core provider metadata only. It does not open a database
connection, check credentials, inspect the schema, or run migrations. Connection
failures after startup remain operational iteration failures and follow the
worker's retry behavior.

## Worker Claiming

The SQL Server EF Core store opens the underlying relational connection when needed and executes SQL Server claim/mark statements.

Claiming is atomic and lease-based. SQL Server uses locking hints and update/output SQL.

If the scoped `DbContext` has a current EF Core transaction, TinyEvents attaches claim and mark commands to that transaction. TinyEvents does not start, commit, or roll back that transaction.

The hosted worker creates a fresh scope for each processing iteration, so claim and mark commands normally run outside an application-owned transaction.

## Migrations

Use normal EF Core migrations:

```bash
dotnet ef migrations add AddTinyEventsOutbox
dotnet ef database update
```

TinyEvents provides mapping. Your application owns migration generation and execution.
