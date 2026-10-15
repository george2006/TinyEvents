# Outbox Operational Status

Requires TinyEvents `1.0.0-beta.2`. See the [upgrade guide](upgrading-to-beta-2.md)
for release availability and compatibility.

`ITinyOutboxStatusReader` reads aggregate state from one configured outbox. Use it
to observe backlog, expired processing leases and terminal failures without loading
event payloads or writing provider-specific SQL in an application.

## Resolve The Reader In A Scope

All four built-in database providers register the reader as scoped through their
existing registration methods. There is no additional registration call or option.
Resolving the service does not query the database or start a background task.

After configuring a provider, a host can read an observation like this:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TinyEvents;

public static class OutboxObservationExample
{
    public static async ValueTask<TinyOutboxStatus> ReadAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITinyOutboxStatusReader>();
        return await reader.ReadAsync(cancellationToken);
    }
}
```

Use an existing application scope instead when the observation must use its EF
Core context and transaction. Do not capture the scoped reader in a singleton or
use the same DbContext concurrently. A singleton hosted service should create a
scope for each observation, as above.

Custom providers and the in-memory store are not required to implement this
separate interface. Publishing and processing contracts are unchanged; resolving
an unregistered reader fails through normal dependency injection behavior.

## Result Semantics

`ReadAsync` returns a `TinyOutboxStatus` with init-only properties. Counts are
`long`; timestamps use `DateTimeOffset`. The reader captures `TimeProvider.GetUtcNow()`
once before accessing the database and uses that value for lease comparisons.

| Property | Meaning |
| --- | --- |
| `ObservedAtUtc` | Application clock time captured at the start of the observation, not query completion time. |
| `PendingCount` | All Pending rows, including retries scheduled in the future. |
| `ProcessingCount` | All Processing rows, including rows whose leases have expired. |
| `ExpiredProcessingCount` | Processing rows with a non-null lease expiry at or before `ObservedAtUtc`. A subset of Processing, not an additional backlog category. |
| `FailedCount` | Terminal Failed rows. Retriable failures returned to Pending are counted as Pending. |
| `OldestPendingAtUtc` | Earliest creation time among **Pending and Processing** rows. Failed and Processed rows are excluded. Null when neither outstanding state exists. |
| `OldestPendingAge` | `ObservedAtUtc - OldestPendingAtUtc`, clamped to zero for future timestamps. Null when there is no outstanding row. |
| `LastProcessedAtUtc` | Latest non-null processing timestamp among **retained Processed rows**. Null when no such timestamp remains. |

An empty outbox has zero counts and null activity timestamps/age. A scheduled retry
contributes to backlog age even when it is not yet eligible for processing.

Cleanup can remove the last retained success, so `LastProcessedAtUtc` is not a
durable heartbeat or historical last-success record. An expired lease indicates
reclaimable work; it does not prove that the original consumer has stopped.
See [workers and leases](workers.md) and [retention and cleanup](retention-and-cleanup.md).

## Connections, Consistency And Errors

| Provider style | Connection and transaction behavior |
| --- | --- |
| EF Core | Borrows the scoped DbContext connection and its active transaction, if present. Leaves the connection owned by the context; does not save changes or track messages. Uncommitted writes in that transaction are visible to the observation. |
| ADO.NET | Obtains a connection from the configured worker connection factory for each call and disposes the returned connection afterwards, including query failures. Does not borrow the configured publisher transaction. |

The query is read-only and respects the connection's isolation settings. It does
not change isolation, add `NOLOCK`, claim messages, renew leases or recover work.
Concurrent processing may affect an observation according to the database's
isolation behavior. Do not use it as a synchronization barrier or proof that an
outbox has drained. An EF context can contain unsaved changes that are not yet
visible to the database query.

Cancellation is passed to connection opening and asynchronous command/reader
operations. Database and configuration failures propagate to the caller; they
are not converted into a healthy-looking empty result. The host decides how to
report unavailable observations and configure time budgets.

## Host Responsibilities And Cost

The result contains counts and timestamps, not payloads, event names, exception
text or connection details. Authentication, authorization, HTTP endpoints,
tenant routing, alert thresholds and polling schedules belong to the host.

One reader observes one database/outbox, not one worker. Several workers sharing
the same outbox will observe the same underlying work: do not sum their counts.
For multiple databases, resolve the appropriate configured scope for each and
bound the number of concurrent reads. TinyEvents does not discover tenants.

Exact counts and oldest-outstanding lookup can scan many index entries. Poll
deliberately, avoid overlapping reads and deduplicate observations of a shared
database. Start with a cadence appropriate to the operational response time you
need and measure it under your workload; registration itself adds no polling.

No new migration or index is required beyond the existing schema version 2.
The [PostgreSQL measurements](performance/postgresql-outbox-status.md) explain why
the existing indexes were retained. They are local synthetic evidence, not a
production SLO or a SQL Server/cloud performance claim.
