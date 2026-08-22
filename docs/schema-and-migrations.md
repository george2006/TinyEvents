# Schema and Migrations

TinyEvents owns forward-only migrations for the outbox schema. Applications explicitly choose when those migrations execute.

Register exactly one TinyEvents database provider, build the host, migrate, and then run it:

```csharp
var host = builder.Build();

await host.Services.MigrateTinyEventsAsync();
await host.RunAsync();
```

`MigrateTinyEventsAsync` creates an asynchronous dependency-injection scope, resolves the provider migrator, applies pending migrations in order, and disposes the scope. It works with ASP.NET Core, worker-only, console, and test hosts.

TinyEvents does not run migrations during service registration or worker startup. Calling the entry point without a registered provider fails with guidance to register exactly one provider.

Built-in migrations are currently forward-only. TinyEvents does not apply down migrations or repair schema drift.

The current schema version is `2`:

- `001_CreateTinyOutbox` creates the outbox and processing indexes.
- `002_AddProcessedCleanupIndex` adds the ordered index used by processed-message cleanup.

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

Provider schemas keep the same logical columns and indexes, but database types follow each provider:

- SQL Server maps `EventType` to `NVARCHAR(512)`, `Payload` to `NVARCHAR(MAX)`, `ClaimedBy` to `NVARCHAR(256)`, and `LastError` to `NVARCHAR(MAX)`.
- PostgreSQL maps `EventType`, `Payload`, `ClaimedBy`, and `LastError` to `text`.

## History And Planning

The history table is named by appending `Migrations` to the configured outbox table name in the resolved schema:

- SQL Server defaults: `dbo.TinyOutbox` and `dbo.TinyOutboxMigrations`
- PostgreSQL defaults: `public.TinyOutbox` and `public.TinyOutboxMigrations`
- Custom example: `app.MyOutbox` and `app.MyOutboxMigrations`

The history table is strict migration state. Once it exists, TinyEvents plans solely from its recorded migration identifiers and applies missing migrations in order.

## EF Core Providers

Keep the outbox mapping in the application's model:

Call:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseTinyEventsOutbox();
}
```

For custom table names:

```csharp
modelBuilder.UseTinyEventsOutbox("app.MyOutbox");
```

The built-in migrator borrows the scoped `DbContext` connection. It opens and closes that connection only when needed and never disposes the application `DbContext` or its connection.

## ADO.NET SQL Server

The SQL Server ADO.NET migrator obtains a dedicated connection from `UseWorkerConnectionFactory(...)`. Calling `MigrateTinyEventsAsync` therefore requires that factory to be configured. TinyEvents owns and disposes the returned migration connection.

For the default table:

```csharp
var sql = TinySqlServerAdoNetSchema.CreateOutboxSql();
```

For a custom table:

```csharp
var sql = TinySqlServerAdoNetSchema.CreateOutboxSql("app.MyOutbox");
```

The SQL helper and packaged script remain available as compatibility assets:

```text
schema/sqlserver/001_CreateTinyOutbox.sql
schema/sqlserver/002_AddProcessedCleanupIndex.sql
```

The built-in migration implementation, not the packaged script, is authoritative for migration planning and execution.

## ADO.NET PostgreSQL

The PostgreSQL ADO.NET migrator obtains a dedicated connection from `UseWorkerConnectionFactory(...)`. Calling `MigrateTinyEventsAsync` therefore requires that factory to be configured. TinyEvents owns and disposes the returned migration connection.

For the default table:

```csharp
var sql = TinyPostgreSqlAdoNetSchema.CreateOutboxSql();
```

For a custom table:

```csharp
var sql = TinyPostgreSqlAdoNetSchema.CreateOutboxSql("app.TinyOutbox");
```

The SQL helper and packaged script remain available as compatibility assets:

```text
schema/postgresql/001_CreateTinyOutbox.sql
schema/postgresql/002_AddProcessedCleanupIndex.sql
```

The built-in migration implementation, not the packaged script, is authoritative for migration planning and execution.

## Upgrading An Existing Alpha Database

The first built-in migration has a deliberately narrow baseline rule for databases created by earlier TinyEvents alpha releases:

- If the history table is absent and the configured outbox table already exists as an ordinary or partitioned table, TinyEvents records migration `001` without executing its SQL.
- If the history table already exists, even when empty, normal history-based planning applies.
- Baselining does not inspect columns, indexes, constraints, or other table shape.

Before upgrading, applications with manually altered schemas must confirm that their existing outbox table matches the expected provider shape. TinyEvents will not infer compatibility or repair drift.

## Migration Logging

Migration execution uses stable `Microsoft.Extensions.Logging` events:

| Event ID | Name | Level | Meaning |
|---:|---|---|---|
| 1400 | `MigrationStarted` | Information | A migration operation started for the configured provider and outbox. |
| 1401 | `MigrationApplied` | Information | One migration was applied or recorded as an existing-alpha baseline. |
| 1402 | `MigrationSchemaCurrent` | Information | Planning found no pending migrations. |
| 1403 | `MigrationCompleted` | Information | Final history inspection confirmed the target version. |
| 1404 | `MigrationFailed` | Error | Migration execution failed. |

Structured properties include provider, schema, table, versions, migration name, baseline status, applied count, and elapsed time. Migration logs do not include SQL, connection strings, credentials, or event payloads. Requested cancellation propagates without being logged as a migration failure.
