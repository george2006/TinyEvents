# PostgreSQL ADO.NET

`TinyEvents.PostgreSql.AdoNet` stores outbox messages through application-owned PostgreSQL transactions.

## Install

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.2
dotnet add package TinyEvents.PostgreSql.AdoNet --version 0.1.0-alpha.2
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

## Worker Connections

`UseWorkerConnectionFactory(...)` is required when the application processes
the outbox, either through `ITinyOutboxProcessor` directly or through the
hosted worker.

The hosted worker validates that the factory is configured before polling. It
does not invoke the delegate or open a database connection during startup
validation. Missing configuration stops startup; failures creating or opening
a configured connection occur during processing and follow the worker's
operational retry behavior.

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
        : new TinyPostgreSqlAdoNetTransactionContext(
            session.Connection,
            session.CurrentTransaction);
});
```

## Migrations

After building the host, explicitly run the built-in migrations:

```csharp
using TinyEvents;

var host = builder.Build();

await host.Services.MigrateTinyEventsAsync();
await host.RunAsync();
```

Migration execution uses the configured worker connection factory. It creates and disposes a dedicated connection; it never uses the application publishing transaction.

The default history table is `public.TinyOutboxMigrations`. Custom outbox tables derive their history table in the same schema.

The SQL helper remains available as a compatibility asset:

```csharp
var sql = TinyPostgreSqlAdoNetSchema.CreateOutboxSql();
```

For custom tables:

```csharp
var sql = TinyPostgreSqlAdoNetSchema.CreateOutboxSql("app.MyOutbox");
```

The package also includes the legacy default PostgreSQL script:

```text
schema/postgresql/001_CreateTinyOutbox.sql
```

The default PostgreSQL schema uses `text` for `EventType`, `Payload`, `ClaimedBy`, and `LastError`.

TinyEvents does not migrate automatically during registration or worker startup.
