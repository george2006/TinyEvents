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

EF Core applications should call:

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

ADO.NET applications should apply the provided SQL Server script from the repository/package content:

```text
src/TinyEvents.SqlServer.AdoNet/Schema/SqlServer/001_CreateTinyOutbox.sql
```

The script creates the default SQL Server shape: `dbo.TinyOutbox`.
If you configure a custom table name or schema, copy or adjust the script to match that name.
TinyEvents owns the schema definition; your application owns when and how the migration runs.

## Future Migration Helpers

If TinyEvents later offers migration helpers, they should live in separate packages, for example:

```text
TinyEvents.Migrations.DbUp
```

Core and provider packages should stay free of migration execution dependencies once provider packages are split.
