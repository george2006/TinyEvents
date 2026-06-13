# TinyEvents.PostgreSql.AdoNet

PostgreSQL ADO.NET provider package for TinyEvents outbox storage and worker claiming.

This package is for applications that own their PostgreSQL connection and transaction. TinyEvents writes the outbox message inside that application-owned transaction, while the worker side uses a separate connection factory to claim and process pending messages.

The provider follows the same ownership model as the SQL Server ADO.NET provider:

- applications own publishing connections and transactions
- TinyEvents writes the outbox message inside the supplied transaction
- TinyEvents does not commit, roll back, open, or dispose the application transaction
- worker operations use provider-owned connections from a configured worker connection factory

PostgreSQL worker claiming uses `FOR UPDATE SKIP LOCKED`.

## Install

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.1
dotnet add package TinyEvents.PostgreSql.AdoNet --version 0.1.0-alpha.1
```

## Register

```csharp
using Npgsql;
using TinyEvents.PostgreSql.AdoNet;

services.UsePostgreSqlAdoNetOutbox(options =>
{
    options.UseCurrentTransaction(sp =>
    {
        var current = sp.GetRequiredService<SampleAdoNetTransaction>();

        return new TinyPostgreSqlAdoNetTransactionContext(
            current.Connection,
            current.Transaction);
    });

    options.UseWorkerConnectionFactory(async (_, ct) =>
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    });
});
```

## Schema

Apply the PostgreSQL schema with your migration tool of choice:

```csharp
var sql = TinyPostgreSqlAdoNetSchema.CreateOutboxSql();
```

For custom tables:

```csharp
var sql = TinyPostgreSqlAdoNetSchema.CreateOutboxSql("app.MyOutbox");
```

The package also includes the default PostgreSQL script:

```text
schema/postgresql/001_CreateTinyOutbox.sql
```

## More Documentation

- Project documentation: https://github.com/george2006/TinyEvents
- ADO.NET provider guide: https://github.com/george2006/TinyEvents/blob/main/docs/postgresql/ado-net.md
- Worker guide: https://github.com/george2006/TinyEvents/blob/main/docs/workers.md
- Schema and migrations: https://github.com/george2006/TinyEvents/blob/main/docs/schema-and-migrations.md
