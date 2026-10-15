# PostgreSQL outbox status measurements

15 October 2026. Query at commit `5fb2908`, PostgreSQL EF Core provider.
This is development evidence for TE1c, not a production latency guarantee.

## Decision

Jorge explicitly discarded the candidate after reviewing the write measurements:
core processing takes priority over operational query latency. Keep schema version 2.
The existing indexes serve the status query. Adding
`(Status, CreatedAtUtc)` saved about 4–5 ms on a 300,000-message backlog in the
controlled comparison, at a cost of 14,213,120 bytes for one million rows plus
index maintenance on writes. The write experiment below reinforces retaining the
current indexes for occasional operational reads. The draft migration 003, model,
catalog and test changes were removed before commit; existing migrations remain intact.

Exact counts and oldest-pending lookup still scale with outstanding work. Hosts
must bound polling across databases and avoid multiplying identical reads across
worker replicas. Revisit this decision using production query plans if observation
cost becomes material. SQL Server requires its own measurements when implemented.

## Environment and method

- Dedicated `postgres:16-alpine` container, PostgreSQL 16.14, 2 CPU limit and 1 GiB
  memory limit, Docker Desktop on Windows; no host port or application database used.
- Default `shared_buffers=128MB`, `max_parallel_workers_per_gather=2`.
- Table and indexes rendered from migrations 001 and 002. The migration runner was
  not under test here. Status SQL extracted from `TinyPostgreSqlEfCoreSql.ReadStatus`.
- Parameters replaced with status values 0/1/2/3 and observation time
  `2026-10-15 12:00:00+00`. Plans use literals; prepared generic plans were not tested.
- One million rows per scenario, 128-character payload, creation times spread over
  24 hours. Half of Pending have future retry dates; half of Processing have expired
  leases. `VACUUM (ANALYZE)` precedes the initial measurements.
- Six `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` executions per measurement: report
  the first and median of the next five. Times are database execution times, excluding
  connection, network, .NET and HTTP costs. First execution is not a controlled cold-cache test.

## Results

| Scenario | Pending | Processing | Failed | Processed | First ms | Subsequent median ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Retained history | 100 | 50 | 10 | 999,840 | 0.503 | 0.311 |
| Large backlog | 250,000 | 50,000 | 10,000 | 690,000 | 195.850 | 30.465 |
| Backlog after updating all Pending/Processing, before vacuum | 250,000 | 50,000 | 10,000 | 690,000 | 201.408 | 114.019 |

The last case temporarily disabled table autovacuum to expose the cost of heap
visibility checks. This is a stress condition, not a steady-state prediction.
Autovacuum was restored afterwards. A fast index-only plan after vacuum does not
mean the same query is always cheap during writes.

The controlled index comparison reseeded the backlog, then measured the same data
without the candidate, with it, and after dropping it:

| Indexes | Subsequent median ms | Shared buffer hits in last plan | Shared blocks read |
| --- | ---: | ---: | ---: |
| Existing, before | 32.406 | 1,503 | 0 |
| Existing plus candidate | 27.217 | 1,092 | 0 |
| Existing, after | 31.130 | 1,502 | 0 |

Observed plan structure with existing indexes:

- Counts use index-only scans over the relevant status range. The large-backlog
  Pending count uses parallel aggregation over `IX_TinyOutbox_ExpiredProcessing`.
- Expired Processing uses both Status and ClaimExpiresAtUtc in that index.
- Oldest pending aggregates the Pending and Processing ranges of
  `IX_TinyOutbox_Pending`; it visits 300,000 entries in the large-backlog case.
- Last retained success uses a backward index-only scan with a one-row limit on
  `IX_TinyOutbox_ProcessedCleanup`. It does not count the retained processed history.
- The candidate changes the oldest-pending scan to its narrower index, but still
  scans the 300,000 outstanding entries for the current `MIN`/`IN` query shape.

## Concurrent processing and timeout

The controlled comparison reseeded and vacuumed the same million-row backlog before
each run, then warmed the status query. One pgbench client ran for 12 seconds with
the provider's claim SQL (50 messages, five-minute lease) followed by a batch completion
UPDATE scoped to that synthetic worker's Processing rows. This tests SQL contention;
it is not the .NET processor, per-message ACK path, or real consumers.

| Run | Completed batches | Completed messages | Batches/s | Mean batch latency ms |
| --- | ---: | ---: | ---: | ---: |
| Without observer, before | 215 | 10,750 | 17.854 | 56.010 |
| With observer | 206 | 10,300 | 17.135 | 58.361 |
| Without observer, after | 211 | 10,550 | 17.515 | 57.094 |

The observer used a separate pgbench client: one status query followed by a one-second
sleep, for 15 seconds. All 15 observations and all worker batches succeeded.
Observed throughput was about 2–4% lower with the observer than the bracketing runs.
These short, synthetic samples do not establish a statistical bound or a production
SLO. The last warm-up median also rose to 41 ms, illustrating environmental variation.
No additional index was present during these runs.

With `statement_timeout='1ms'`, the large-backlog query failed with PostgreSQL SQLSTATE
`57014` (`canceling statement due to statement timeout`). This validates the database
timeout, not in-flight .NET token cancellation. TE1b separately covers a cancelled
token before connection opening and database error propagation.

TE1c retains no migration, production SQL or application code changes. Its final
result is this evidence and the decision to keep the existing schema. After removing
the draft, the full solution builds in Release with zero warnings/errors; all eight
migrator integration tests pass on each real database (PostgreSQL and SQL Server),
with no skips. The disposable measurement container has been removed.

## Write cost

Using the same PostgreSQL limits and data distribution, compare existing indexes,
then the candidate, then existing indexes again. Each pass truncates and loads a
fresh million rows, vacuums, then updates all 250,000 Pending rows to Processing,
setting ClaimedBy and ClaimExpiresAtUtc. Timings and WAL bytes come from
`EXPLAIN (ANALYZE, BUFFERS, WAL, FORMAT JSON)` on the INSERT and UPDATE.

| Indexes | Insert 1M rows ms | Insert WAL bytes | Update 250k rows ms | Update WAL bytes |
| --- | ---: | ---: | ---: | ---: |
| Existing, before | 9,956.939 | 751,796,310 | 2,908.803 | 256,535,191 |
| With candidate | 10,467.707 | 853,771,215 | 3,146.770 | 277,953,632 |
| Existing, after | 9,439.750 | 763,436,581 | 2,799.226 | 256,535,864 |

The candidate took roughly 5–11% longer for bulk insertion and 8–12% longer for the
bulk state update than the bracketing runs. WAL grew about 12–14% and 8%, respectively.
These are single bulk statements on synthetic data, not a claim that real worker
throughput falls by the same percentages. They do establish a write-side cost that
we choose not to impose for this operational read. No SQL Server performance claim
is made: its candidate was withdrawn as well.

## Reproducing the data

Use an isolated database with the current outbox migrations. In `psql`, set
`pending`, `processing`, and `failed` from the scenario table, then run:

```sql
\set pending 250000
\set processing 50000
\set failed 10000

TRUNCATE "TinyOutbox";
INSERT INTO "TinyOutbox"
    ("Id", "EventType", "Payload", "Status", "AttemptCount", "CreatedAtUtc",
     "NextAttemptAtUtc", "ClaimedBy", "ClaimedAtUtc", "ClaimExpiresAtUtc", "ProcessedAtUtc")
SELECT md5(n::text)::uuid, 'MeasurementEvent', repeat('x', 128), status, 0,
       observed - (n % 86400) * INTERVAL '1 second',
       CASE WHEN status = 0 AND n % 2 = 0 THEN observed + INTERVAL '1 hour' END,
       CASE WHEN status = 1 THEN 'existing-worker' END,
       CASE WHEN status = 1 THEN observed - INTERVAL '5 minutes' END,
       CASE WHEN status = 1 THEN observed + CASE WHEN n % 2 = 0
           THEN INTERVAL '-1 minute' ELSE INTERVAL '1 minute' END END,
       CASE WHEN status = 2 THEN observed - (n % 3600) * INTERVAL '1 second' END
FROM (
    SELECT n, TIMESTAMPTZ '2026-10-15 12:00:00+00' AS observed,
           CASE WHEN n <= :pending THEN 0
                WHEN n <= :pending + :processing THEN 1
                WHEN n <= :pending + :processing + :failed THEN 3
                ELSE 2 END AS status
    FROM generate_series(1, 1000000) n
) rows;
VACUUM (ANALYZE) "TinyOutbox";
```

Prefix the current [status query](../../src/TinyEvents.PostgreSql.EntityFrameworkCore/Sql/TinyPostgreSqlEfCoreSql.cs)
with `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` using the parameters above. Compare
before/after `CREATE INDEX` on `(Status, CreatedAtUtc)` without changing the data.
For the pre-vacuum case, disable table autovacuum temporarily and update AttemptCount
on Status 0/1 before observing. Restore autovacuum and vacuum before other comparisons.
