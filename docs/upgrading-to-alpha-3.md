# Upgrading To 0.1.0-alpha.3

This guide covers upgrading an application and database from TinyEvents
`0.1.0-alpha.2` to `0.1.0-alpha.3`.

The `alpha.3` release introduces built-in, forward-only outbox migrations. An
existing `alpha.2` outbox is preserved and recorded as the initial migration
baseline.

## Before Upgrading

- Back up the application database.
- Confirm that the existing outbox was created from the unmodified `alpha.2`
  SQL helper or EF Core mapping.
- Resolve manually altered, incomplete, or incompatible outbox schemas before
  running TinyEvents migrations. Baselining does not inspect or repair table
  shape.
- Plan an explicit migration point before workers begin processing.

## Upgrade The Complete Package Train

Keep every TinyEvents package on the same version. For example:

```bash
dotnet add package TinyEvents --version 0.1.0-alpha.3
dotnet add package TinyEvents.SqlServer.EntityFrameworkCore --version 0.1.0-alpha.3
dotnet add package TinyEvents.Worker --version 0.1.0-alpha.3
```

Use the matching `TinyEvents.SqlServer.*` or `TinyEvents.PostgreSql.*` provider
package for the application's database and persistence style. Register exactly
one TinyEvents database provider.

## Configure Migration Connectivity

EF Core providers borrow the registered scoped `DbContext` connection. No
additional migration connection configuration is required.

ADO.NET providers reuse `UseWorkerConnectionFactory(...)`. The factory is
required before calling `MigrateTinyEventsAsync` and returns a dedicated
connection that TinyEvents disposes after migration execution.

## Run Migrations Explicitly

Build the host, migrate, and only then run the host:

```csharp
using TinyEvents;

var host = builder.Build();

await host.Services.MigrateTinyEventsAsync();
await host.RunAsync();
```

TinyEvents does not migrate during service registration or worker startup.

## Existing Alpha Schema Behavior

On the first `alpha.3` migration call:

- if the migration history table is absent and the configured outbox table
  already exists, migration `001` is recorded without recreating the outbox;
- if the history table already exists, normal strict history planning applies;
- subsequent calls are idempotent and observe the schema as current.

The default table pairs are:

- SQL Server: `dbo.TinyOutbox` and `dbo.TinyOutboxMigrations`;
- PostgreSQL: `public.TinyOutbox` and `public.TinyOutboxMigrations`.

Custom outbox tables derive the history table by appending `Migrations` in the
same schema.

## Verify The Upgrade

After migration execution:

1. Confirm the original outbox table and rows remain present.
2. Confirm the derived history table contains migration `001` exactly once.
3. Run the application and verify normal publishing and worker processing.
4. Call `MigrateTinyEventsAsync` again and confirm it completes as a no-op.

The repository validates this journey with the actual published `alpha.2`
packages and locally packed `alpha.3` packages for both SQL Server and
PostgreSQL:

```powershell
.\samples\TinyEvents.PackageSmoke\Test-AlphaUpgrade.ps1
```

See [Schema and Migrations](schema-and-migrations.md) for the complete migration
contract and non-goals.
