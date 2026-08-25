# Retention and Cleanup

> **Release status:** This capability is included in `1.0.0-beta.1`.

TinyEvents automatically removes processed outbox messages after a configurable
retention period. Cleanup keeps the operational outbox bounded without turning
it into an audit or observability store.

## Default Policy

The following defaults are accepted for the beta:

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

The beta laboratory measured processed rows containing 1 KB of deterministic,
compression-resistant content at approximately 4,515 bytes per row on SQL
Server and 1,951 bytes per row on PostgreSQL. Applying those local measurements
to the one-hour default produces this planning model:

```text
retained rows = processed messages/second * retention seconds
processed storage = retained rows * measured processed bytes/row
```

| Sustained processed rate | Rows retained | SQL Server | PostgreSQL |
|---:|---:|---:|---:|
| 200 messages/s | 720,000 | 3.25 GB | 1.40 GB |
| 400 messages/s | 1,440,000 | 6.50 GB | 2.81 GB |
| 800 messages/s | 2,880,000 | 13.00 GB | 5.62 GB |

These decimal-GB projections cover only the measured outbox table and indexes.
They are not database-size guarantees: real event metadata and payloads may be
larger, database allocation is engine-specific, and pending, processing, and
failed rows sit outside the processed-retention window. Measure the real
application before choosing its budget. Lower `ProcessedRetention` when the
modeled window is larger than the available budget.

Choose retention and batch settings from measured workload evidence. The
configured cleanup capacity is approximately:

```text
CleanupBatchSize / CleanupInterval
```

The accepted defaults can attempt one batch of 1,000 rows each second from each
application instance. Four-process beta runs deleted between 2,170 and 3,910
eligible rows per second while active work continued. At 200 and 400 requests
per second, cleanup-enabled publishing throughput remained within 0.2% of the
cleanup-disabled baseline on both providers. PostgreSQL also sustained 800;
the local SQL Server environment did not sustain 800 with or without cleanup,
and cleanup increased latency while the database caught up with 50,000 expired
rows.

That catch-up result is why the defaults remain configurable. Reducing
`CleanupBatchSize` or increasing `CleanupInterval` reduces cleanup pressure but
also lowers cleanup capacity. Keep the configured capacity above the expected
processed-message rate, and validate it against the application's database.

The public [TinyEvents Dogfood laboratory](https://github.com/george2006/TinyEvents.DogFood)
contains the executable storage and cleanup-under-load contracts behind these
measurements.

## Schema Requirement

Migration `002_AddProcessedCleanupIndex` adds the ordered lookup used by
cleanup. Run `MigrateTinyEventsAsync` before starting a worker built with this
feature.

The migration preserves the initial migration checksum and upgrades existing
alpha outboxes through normal forward-only migration planning.
