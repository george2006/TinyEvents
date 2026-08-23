# Retention and Cleanup

> **Release status:** This capability is implemented for the next TinyEvents
> release. It is not included in the latest published NuGet packages yet.

TinyEvents automatically removes processed outbox messages after a configurable
retention period. Cleanup keeps the operational outbox bounded without turning
it into an audit or observability store.

## Candidate Default Policy

The hosted worker currently uses the following candidate defaults for the next
release. Beta load hardening will determine whether these values are accepted
or changed before publication:

```csharp
services.AddTinyEventsWorker(options =>
{
    options.CleanupEnabled = true;
    options.ProcessedRetention = TimeSpan.FromHours(1);
    options.CleanupBatchSize = 1_000;
    options.CleanupInterval = TimeSpan.FromSeconds(1);
});
```

A message is eligible only when:

- its status is `Processed`; and
- `ProcessedAtUtc < now - ProcessedRetention`.

The boundary is intentionally exclusive. A message whose `ProcessedAtUtc`
equals the cutoff remains until a later cleanup iteration.

Cleanup never deletes `Pending`, `Processing`, or `Failed` messages. TinyEvents
v1 retains failed messages because the current schema does not record an
authoritative terminal-failure timestamp. Applications should monitor failed
row growth and investigate or remove failed rows using an explicit operational
procedure.

## Runtime Behavior

Processing and cleanup run in separate hosted-service loops. A cleanup failure
does not stop event processing. Cleanup opens a dependency-injection scope and
executes at most one bounded delete batch per interval.

SQL Server uses an atomic ordered delete with row-level update locks and
`READPAST`. PostgreSQL selects the bounded batch with
`FOR UPDATE SKIP LOCKED` and deletes it in the same statement. Multiple
application instances can therefore clean concurrently without a leader,
cleanup claim, or lease. If a cleanup command fails, its database transaction
does not partially commit the batch.

Built-in SQL Server and PostgreSQL providers implement cleanup for both ADO.NET
and EF Core. A custom outbox provider used with `TinyEvents.Worker` must also
register `ITinyOutboxCleanupStore`. Set `CleanupEnabled = false` when an
application deliberately owns retention itself or while upgrading a custom
provider that does not yet implement cleanup.

## Storage Budget

Retention is time-based, not byte-based. Database engine, payload size, indexes,
write rate, and failure rate all affect storage. TinyEvents does not promise a
universal database-size ceiling.

Choose retention and batch settings from measured workload evidence. The
configured cleanup capacity is approximately:

```text
CleanupBatchSize / CleanupInterval
```

For example, the candidate defaults can attempt one batch of 1,000 rows each
second. Real capacity must still be verified against the application's database
and concurrent workload.

## Schema Requirement

Migration `002_AddProcessedCleanupIndex` adds the ordered lookup used by
cleanup. Run `MigrateTinyEventsAsync` before starting a worker built with this
feature.

The migration preserves the initial migration checksum and upgrades existing
alpha outboxes through normal forward-only migration planning.
