# Upgrading To 1.0.0-beta.1

This guide covers upgrading an application and database from TinyEvents
`0.1.0-alpha.3` to `1.0.0-beta.1`.

The beta adds bounded processed-message cleanup, durable event-name aliases,
stronger schema validation, and the reliability evidence gathered against SQL
Server and PostgreSQL. It remains a pre-1.0 release.

## Upgrade The Package Train Together

Keep every TinyEvents package used by an application on the same version. For
example:

```bash
dotnet add package TinyEvents --version 1.0.0-beta.1
dotnet add package TinyEvents.SqlServer.EntityFrameworkCore --version 1.0.0-beta.1
dotnet add package TinyEvents.Worker --version 1.0.0-beta.1
```

Use the matching SQL Server or PostgreSQL provider and the matching EF Core or
ADO.NET integration for the application.

## Review Cleanup Before Starting The Beta Worker

`1.0.0-beta.1` enables bounded cleanup of processed messages by default:

```csharp
builder.Services.AddTinyEventsWorker(options =>
{
    options.CleanupEnabled = true;
    options.ProcessedRetention = TimeSpan.FromHours(1);
    options.CleanupBatchSize = 1_000;
    options.CleanupInterval = TimeSpan.FromSeconds(1);
});
```

Cleanup never removes pending, processing, or failed messages. Review the
[retention and cleanup](retention-and-cleanup.md) guidance against the real
event rate, payload size, retention need, and database capacity.

Set `CleanupEnabled = false` when the application deliberately owns retention
or while a custom provider does not implement `ITinyOutboxCleanupStore`.

## Apply Migration 002 Before Running The Host

Migration `002_AddProcessedCleanupIndex` adds the ordered index used by cleanup.
Build the host, explicitly migrate, and only then start its hosted services:

```csharp
var host = builder.Build();

await host.Services.MigrateTinyEventsAsync();
await host.RunAsync();
```

`AddTinyEventsWorker(...)` registers independent processing and cleanup hosted
services. Registration does not start them, and TinyEvents never applies schema
migrations implicitly during worker startup.

For ADO.NET providers, configure `UseWorkerConnectionFactory(...)` before
calling `MigrateTinyEventsAsync`. EF Core providers borrow the registered scoped
`DbContext` connection for migration execution.

## Rolling Upgrade Boundary

The supported adjacent-version path for this additive migration is:

1. Keep already-running `alpha.3` workers online.
2. Start a `beta.1` instance and let it apply migration `002`.
3. Allow the running `alpha.3` and `beta.1` workers to process during the
   transition.
4. Replace the remaining `alpha.3` instances with `beta.1`.

Do not restart an `alpha.3` binary after migration `002` has been applied. A
newly started older binary fails fast because the database is newer than its
migration catalog. Start that instance with `beta.1` instead.

## Preserve Durable Event Names During Refactors

The stored event name is a durable contract. Before renaming an event type or
namespace, register its previous name:

```csharp
builder.Services.UseTinyEvents(options =>
{
    options.AcceptPreviousEventName<OrderCreated>(
        "Old.Namespace.OrderCreated");
});
```

Deploy the mapping before the rename and retain it while messages with the old
name can remain pending or eligible for retry. Generic event contracts are not
supported; source-generator diagnostic `TEV002` reports them during compilation.

## Verify The Upgrade

1. Confirm all TinyEvents package references resolve to `1.0.0-beta.1`.
2. Back up the database according to the application's normal deployment policy.
3. Run `MigrateTinyEventsAsync` and confirm migration `002` is recorded once.
4. Start the host and verify publishing and worker processing.
5. Confirm processed rows older than the configured retention are removed while
   pending, processing, and failed rows remain.
6. Call `MigrateTinyEventsAsync` again and confirm it completes as a no-op.

The complete beta guarantees and accepted limitations are documented in the
[README reliability contract](../README.md#reliability-contract).
