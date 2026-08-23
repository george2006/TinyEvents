# TinyEvents.SqlServer.AdoNet

SQL Server ADO.NET provider for TinyEvents outbox storage and worker claiming.

This package is for applications that own their ADO.NET connection and transaction. TinyEvents writes the outbox message inside that application-owned transaction, while the worker side uses a separate connection factory to claim and process pending messages.

## Install

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.3
dotnet add package TinyEvents.SqlServer.AdoNet --version 0.1.0-alpha.3
```

## Register

```csharp
using Microsoft.Data.SqlClient;
using TinyEvents.SqlServer.AdoNet;

services.UseSqlServerAdoNetOutbox(options =>
{
    options.UseCurrentTransaction(sp =>
    {
        var current = sp.GetRequiredService<SampleAdoNetTransaction>();

        return new TinyAdoNetTransactionContext(
            current.Connection,
            current.Transaction);
    });

    options.UseWorkerConnectionFactory(async (_, ct) =>
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    });
});
```

Register exactly one TinyEvents database provider per service collection.

## Transaction Ownership

TinyEvents does not open, begin, commit, roll back, or dispose the application transaction. Your application owns the persistence boundary.

Worker connections returned by `UseWorkerConnectionFactory(...)` are owned by TinyEvents for that worker operation and may be disposed after use.

## Migrations

After building the host, explicitly apply the built-in SQL Server migrations before starting it:

```csharp
using TinyEvents;

var host = builder.Build();

await host.Services.MigrateTinyEventsAsync();
await host.RunAsync();
```

The migrator uses `UseWorkerConnectionFactory(...)` to create a dedicated connection and disposes it after the operation. The factory is therefore required for both worker processing and migration execution.

The default tables are `dbo.TinyOutbox` and `dbo.TinyOutboxMigrations`. A custom outbox such as `app.MyOutbox` uses `app.MyOutboxMigrations`.

Processed-message cleanup is implemented for the next release and is not in the
latest published packages. That release adds migration
`002_AddProcessedCleanupIndex`; `MigrateTinyEventsAsync` applies it as a normal
forward-only migration when it is not already recorded.

The SQL helper remains available as a compatibility asset:

```csharp
var sql = TinySqlServerAdoNetSchema.CreateOutboxSql();
```

For custom tables:

```csharp
var sql = TinySqlServerAdoNetSchema.CreateOutboxSql("app.MyOutbox");
```

The package also includes the legacy default SQL Server script:

```text
schema/sqlserver/001_CreateTinyOutbox.sql
```

The default table is `dbo.TinyOutbox`.

TinyEvents never applies migrations automatically during service registration or worker startup.

## More Documentation

- ADO.NET provider guide: https://github.com/george2006/TinyEvents/blob/main/docs/sql-server/ado-net.md
- Schema and migrations: https://github.com/george2006/TinyEvents/blob/main/docs/schema-and-migrations.md
- Worker guide: https://github.com/george2006/TinyEvents/blob/main/docs/workers.md
- Retention and cleanup: https://github.com/george2006/TinyEvents/blob/main/docs/retention-and-cleanup.md
