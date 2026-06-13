# TinyEvents.SqlServer.AdoNet

SQL Server ADO.NET provider for TinyEvents outbox storage and worker claiming.

This package is for applications that own their ADO.NET connection and transaction. TinyEvents writes the outbox message inside that application-owned transaction, while the worker side uses a separate connection factory to claim and process pending messages.

## Install

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.2
dotnet add package TinyEvents.SqlServer.AdoNet --version 0.1.0-alpha.2
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

## Transaction Ownership

TinyEvents does not open, begin, commit, roll back, or dispose the application transaction. Your application owns the persistence boundary.

Worker connections returned by `UseWorkerConnectionFactory(...)` are owned by TinyEvents for that worker operation and may be disposed after use.

## Schema

Apply the SQL Server schema with your migration tool of choice:

```csharp
var sql = TinySqlServerAdoNetSchema.CreateOutboxSql();
```

For custom tables:

```csharp
var sql = TinySqlServerAdoNetSchema.CreateOutboxSql("app.MyOutbox");
```

The package also includes the default SQL Server script:

```text
schema/sqlserver/001_CreateTinyOutbox.sql
```

The default table is `dbo.TinyOutbox`.

## More Documentation

- ADO.NET provider guide: https://github.com/george2006/TinyEvents/blob/main/docs/sql-server/ado-net.md
- Schema and migrations: https://github.com/george2006/TinyEvents/blob/main/docs/schema-and-migrations.md
- Worker guide: https://github.com/george2006/TinyEvents/blob/main/docs/workers.md
