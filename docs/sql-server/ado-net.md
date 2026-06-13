# SQL Server ADO.NET

`TinyEvents.SqlServer.AdoNet` stores outbox messages through application-owned SQL Server transactions.

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

## Publishing Transaction Ownership

TinyEvents:

- inserts the outbox row using the supplied transaction
- does not open the application connection
- does not begin the transaction
- does not commit
- does not roll back
- does not dispose the application transaction

The application owns the persistence boundary.

`SampleAdoNetTransaction` is application infrastructure. It is not a TinyEvents abstraction.

## Existing Unit Of Work Or Session

If your application already owns a database session, map it directly:

```csharp
options.UseCurrentTransaction(sp =>
{
    var session = sp.GetRequiredService<IApplicationDbSession>();

    return session.CurrentTransaction is null
        ? null
        : new TinyAdoNetTransactionContext(
            session.Connection,
            session.CurrentTransaction);
});
```

## Schema Script

For code-based migration runners:

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
