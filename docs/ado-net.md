# ADO.NET Provider

`TinyEvents.SqlServer.AdoNet` stores outbox messages through application-owned ADO.NET transactions.

The current alpha provider emits SQL Server SQL.

## Install

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.1
dotnet add package TinyEvents.SqlServer.AdoNet --version 0.1.0-alpha.1
```

The ADO.NET provider package is SQL Server-specific.

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

This registers TinyEvents core services, applies generated consumer contributions, and configures the ADO.NET outbox writer/store.

## Publishing Transaction Ownership

ADO.NET publishing requires an application-owned `DbConnection` and `DbTransaction`.

TinyEvents:

- inserts the outbox row using the supplied transaction
- does not open the application connection
- does not begin the transaction
- does not commit
- does not roll back
- does not dispose the application transaction

The application owns the persistence boundary.

## Simple Application Shape

For small applications without an existing session abstraction, create a scoped transaction object in the composition root:

```csharp
services.AddScoped<SampleAdoNetTransaction>(_ =>
{
    var connection = new SqlConnection(connectionString);
    connection.Open();
    var transaction = connection.BeginTransaction();

    return new SampleAdoNetTransaction(connection, transaction);
});
```

Then use it from a use case:

```csharp
public sealed class RegisterUserUseCase
{
    private readonly SampleAdoNetTransaction transaction;
    private readonly ITinyEventPublisher events;

    public RegisterUserUseCase(
        SampleAdoNetTransaction transaction,
        ITinyEventPublisher events)
    {
        this.transaction = transaction;
        this.events = events;
    }

    public async ValueTask RegisterAsync(string email, CancellationToken ct)
    {
        await InsertUserAsync(transaction.Connection, transaction.Transaction, email, ct);
        await events.PublishAsync(new UserCreated(Guid.NewGuid(), email), ct);
        await transaction.CommitAsync(ct);
    }
}
```

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

TinyEvents does not ask your app to adopt a TinyEvents unit of work.

## Worker Connection Factory

Worker operations use a separate connection factory:

```csharp
options.UseWorkerConnectionFactory(async (sp, ct) =>
{
    var factory = sp.GetRequiredService<IApplicationDbConnectionFactory>();
    return await factory.CreateOpenConnectionAsync(ct);
});
```

Connections returned by `UseWorkerConnectionFactory(...)` are owned by TinyEvents for that worker operation and may be disposed after the operation.

Contexts returned by `UseCurrentTransaction(...)` are owned by the application and are never disposed by TinyEvents.

## Schema Script

ADO.NET applications should apply the provided SQL Server script with their migration tool of choice:

```text
src/TinyEvents.SqlServer.AdoNet/Schema/SqlServer/001_CreateTinyOutbox.sql
```

The script creates the default SQL Server shape: `dbo.TinyOutbox`.
If you configure a custom table name or schema, copy or adjust the script to match that name.
TinyEvents owns the schema definition; your application owns when and how the migration runs.
