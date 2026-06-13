# Schema and Migrations

TinyEvents owns the outbox schema definition. Applications own schema migration execution.

TinyEvents does not:

- run migrations automatically
- create migration history tables
- depend on DbUp, Flyway, Liquibase, or another migration engine
- apply schema changes at startup

## Outbox Table

The outbox message shape is:

- `Id`
- `EventType`
- `Payload`
- `Status`
- `AttemptCount`
- `ClaimedBy`
- `ClaimedAtUtc`
- `ClaimExpiresAtUtc`
- `CreatedAtUtc`
- `NextAttemptAtUtc`
- `ProcessedAtUtc`
- `LastError`

The v1 statuses are:

- `Pending`
- `Processing`
- `Processed`
- `Failed`

## EF Core

EF Core applications create the schema through normal EF Core migrations.

Call:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseTinyEventsOutbox();
}
```

Then use normal EF migrations:

```bash
dotnet ef migrations add AddTinyEventsOutbox
dotnet ef database update
```

For custom table names:

```csharp
modelBuilder.UseTinyEventsOutbox("app.MyOutbox");
```

## ADO.NET SQL Server

ADO.NET applications create the schema by running the SQL Server migration script through their migration tool of choice.

For the default table:

```csharp
var sql = TinySqlServerAdoNetSchema.CreateOutboxSql();
```

For a custom table:

```csharp
var sql = TinySqlServerAdoNetSchema.CreateOutboxSql("app.MyOutbox");
```

Run that SQL through DbUp, Flyway, Liquibase, your deployment pipeline, or your existing application migration runner.

The package also includes the default SQL script as package content:

```text
schema/sqlserver/001_CreateTinyOutbox.sql
```

The script creates the default SQL Server shape: `dbo.TinyOutbox`.
TinyEvents owns the schema definition; your application owns when and how the migration runs.

## ADO.NET PostgreSQL

ADO.NET applications create the schema by running the PostgreSQL migration script through their migration tool of choice.

For the default table:

```csharp
var sql = TinyPostgreSqlAdoNetSchema.CreateOutboxSql();
```

For a custom table:

```csharp
var sql = TinyPostgreSqlAdoNetSchema.CreateOutboxSql("app.TinyOutbox");
```

Run that SQL through DbUp, Flyway, Liquibase, your deployment pipeline, or your existing application migration runner.

The package also includes the default PostgreSQL script as package content:

```text
schema/postgresql/001_CreateTinyOutbox.sql
```

The script creates the default PostgreSQL shape: `public.TinyOutbox`.
TinyEvents owns the schema definition; your application owns when and how the migration runs.

## Future Migration Helpers

If TinyEvents later offers migration helpers, they should live in separate packages, for example:

```text
TinyEvents.Migrations.DbUp
```

Core and provider packages should stay free of migration execution dependencies.
