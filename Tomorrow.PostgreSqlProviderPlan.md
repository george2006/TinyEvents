# Tomorrow: PostgreSQL Provider

Goal: add PostgreSQL support to TinyEvents without disturbing the current SQL Server provider shape.

Branch: `plan/postgres-provider`
Base: latest `main` pulled on 2026-06-13.
Scope: analysis first, implementation later in small reviewed slices.

This is an analysis and implementation plan. It should guide small, testable slices. No slice should depend on hope. If a slice changes behavior, it should include a focused test that proves the behavior.

Use the same code standard as TinyDispatcher:

- calm
- explicit
- readable top down
- boring in the best way
- behavior tests over structural tests
- object-oriented code where it clarifies responsibility
- no leaky abstractions introduced only to make unit tests easy
- unit tests only where they add real design or behavioral value
- no clever abstractions unless they remove real complexity

Think like principal engineers working on an OSS package: public APIs should be boring, durable, and easy to reason about under pressure.

## Product Goal

TinyEvents should support PostgreSQL as a first-class outbox database provider.

The new provider should let applications:

- publish outbox messages through EF Core using Npgsql
- publish outbox messages through ADO.NET using Npgsql
- run TinyEvents workers against PostgreSQL
- claim pending messages atomically with PostgreSQL locking semantics
- create the outbox schema through EF Core migrations or a provider SQL script

The core TinyEvents package should remain provider-agnostic.

## Proposed Packages

Mirror the SQL Server package boundaries:

```text
TinyEvents.PostgreSql.EntityFrameworkCore
TinyEvents.PostgreSql.AdoNet
```

Keep `TinyEvents.Worker` unchanged. The worker should continue to depend only on core abstractions:

```text
ITinyOutboxStore
ITinyOutboxProcessor
IEventConsumer<TEvent>
```

## Provider Dependencies

Expected package dependencies:

```text
TinyEvents.PostgreSql.EntityFrameworkCore
  -> Npgsql.EntityFrameworkCore.PostgreSQL
  -> Microsoft.EntityFrameworkCore.Relational
  -> TinyEvents

TinyEvents.PostgreSql.AdoNet
  -> Npgsql
  -> TinyEvents
```

Test dependencies:

```text
Testcontainers.PostgreSql
Npgsql
Npgsql.EntityFrameworkCore.PostgreSQL
```

## Database Semantics

PostgreSQL claiming should use row-level locking with `FOR UPDATE SKIP LOCKED`.

The important rule remains:

> Query-then-update claiming is not acceptable.

The likely shape is:

```sql
WITH claimed AS
(
    SELECT "Id"
    FROM "TinyOutbox"
    WHERE
        (
            "Status" = @PendingStatus
            AND ("NextAttemptAtUtc" IS NULL OR "NextAttemptAtUtc" <= @Now)
        )
        OR
        (
            "Status" = @ProcessingStatus
            AND "ClaimExpiresAtUtc" <= @Now
        )
    ORDER BY "CreatedAtUtc"
    FOR UPDATE SKIP LOCKED
    LIMIT @BatchSize
)
UPDATE "TinyOutbox" AS outbox
SET
    "Status" = @ProcessingStatus,
    "ClaimedBy" = @WorkerId,
    "ClaimedAtUtc" = @Now,
    "ClaimExpiresAtUtc" = @ClaimExpiresAtUtc
FROM claimed
WHERE outbox."Id" = claimed."Id"
RETURNING
    outbox."Id",
    outbox."EventType",
    outbox."Payload",
    outbox."Status",
    outbox."AttemptCount",
    outbox."ClaimedBy",
    outbox."ClaimedAtUtc",
    outbox."ClaimExpiresAtUtc",
    outbox."CreatedAtUtc",
    outbox."NextAttemptAtUtc",
    outbox."ProcessedAtUtc",
    outbox."LastError";
```

This is the PostgreSQL equivalent of the SQL Server `UPDATE ... OUTPUT` claim pattern.

## Schema Direction

Start with the same logical outbox shape as SQL Server:

- `Id`
- `EventType`
- `Payload`
- `Status`
- `AttemptCount`
- `ClaimedBy`
- `ClaimedAtUtc`
- `ClaimExpiresAtUtc`
- `CreatedAtUtc`
- `NextAttemptAtUtc`
- `ProcessedAtUtc`
- `LastError`

Use quoted identifiers in generated SQL so the provider can match the existing PascalCase model cleanly.

Default table:

```text
public.TinyOutbox
```

Provider options should allow custom table names, matching the SQL Server provider pattern.

Open naming decision:

- Provider namespace and package should use `PostgreSql` for .NET naming consistency.
- User-facing docs can say PostgreSQL.

## High-Level Implementation Strategy

Do not start by abstracting over SQL Server and PostgreSQL.

First, mirror the existing provider shape. Once both providers exist and tests are green, review duplicated code calmly. Only extract shared helpers if they clearly reduce real complexity.

The first implementation should be easy to compare with SQL Server:

```text
SqlServer provider
PostgreSql provider
```

That keeps review simple and avoids accidental core changes.

## Slice Plan

### Slice 1: Planning Document

Goal: capture the design before changing code.

Work:

- Add this plan.
- Do not add provider projects yet.

Tests:

- None. This is documentation only.

Commit:

```text
Add PostgreSQL provider implementation plan
```

### Slice 2: Add Project Skeletons

Goal: create provider package boundaries without behavior.

Work:

- Add `src/TinyEvents.PostgreSql.EntityFrameworkCore`.
- Add `src/TinyEvents.PostgreSql.AdoNet`.
- Add matching empty test projects:
  - `tests/TinyEvents.PostgreSql.EntityFrameworkCore.Tests`
  - `tests/TinyEvents.PostgreSql.AdoNet.Tests`
  - optionally one combined integration project:
    `tests/TinyEvents.PostgreSql.Tests`
- Add projects to `TinyEvents.sln`.
- Add package metadata mirroring SQL Server packages.

Tests:

- `dotnet test` should still pass.
- Add one trivial package-boundary test per new provider only if useful.

Commit:

```text
Add PostgreSQL provider project skeletons
```

### Slice 3: PostgreSQL Table Name Handling

Goal: safely parse and quote PostgreSQL table names.

Work:

- Add `TinyPostgreSqlAdoNetTableName`.
- Add `TinyPostgreSqlEfCoreTableName` or a shared provider-local helper if both packages need the same behavior.
- Support:
  - `TinyOutbox`
  - `public.TinyOutbox`
  - custom schema/table names
- Generate quoted SQL names safely.

Tests:

- parses default table name
- parses schema-qualified table name
- rejects null or whitespace
- quotes identifiers correctly
- rejects unsupported multipart names

Commit:

```text
Add PostgreSQL table name parsing
```

### Slice 4: ADO.NET Schema Script

Goal: provide a PostgreSQL schema script before runtime logic.

Work:

- Add `TinyPostgreSqlAdoNetSchema.CreateOutboxSql()`.
- Add packed script:
  - `schema/postgresql/001_CreateTinyOutbox.sql`
- Use PostgreSQL column types:
  - `uuid`
  - `text`
  - `integer`
  - `timestamp with time zone`
- Add useful indexes for claiming:
  - pending due messages
  - expired processing messages

Tests:

- default schema SQL contains expected table and indexes
- custom table SQL contains quoted custom name
- null/invalid table names are rejected

Commit:

```text
Add PostgreSQL outbox schema script
```

### Slice 5: ADO.NET Writer

Goal: insert outbox messages through an application-owned Npgsql transaction.

Work:

- Add PostgreSQL ADO.NET options.
- Add transaction context types if SQL Server types cannot be reused cleanly.
- Add `TinyPostgreSqlAdoNetOutboxWriter`.
- Add insert SQL and parameter helpers.
- Keep transaction ownership rules identical to SQL Server:
  - app owns connection
  - app owns transaction
  - TinyEvents does not commit, roll back, open, or dispose it

Tests:

- writer rejects null message
- writer requires configured current transaction
- insert participates in caller transaction
- rollback removes inserted outbox row
- commit persists inserted outbox row

Commit:

```text
Add PostgreSQL ADO.NET outbox writer
```

### Slice 6: ADO.NET Store Claiming

Goal: implement atomic PostgreSQL worker claiming.

Work:

- Add worker connection factory.
- Add `TinyPostgreSqlAdoNetOutboxStore`.
- Implement:
  - `ClaimPendingAsync`
  - `MarkProcessedAsync`
  - `MarkFailedAsync`
- Use `FOR UPDATE SKIP LOCKED` inside an atomic `WITH claimed AS (...) UPDATE ... RETURNING` statement.

Tests:

- claims due pending messages
- skips pending messages scheduled in the future
- skips active processing messages
- reclaims expired processing messages
- sets worker id and claim expiration
- returns claimed rows
- two workers do not claim the same unexpired row
- mark processed only works for the owning worker
- mark failed only works for the owning worker
- failed without retry becomes `Failed`
- failed with retry becomes `Pending`

Commit:

```text
Add PostgreSQL ADO.NET worker store
```

### Slice 7: EF Core Mapping

Goal: support EF Core schema generation and publishing.

Work:

- Add `TinyEventsModelBuilderExtensions` for PostgreSQL provider.
- Map `TinyOutboxMessage`.
- Set table name and column constraints.
- Keep API parallel to SQL Server:

```csharp
modelBuilder.UseTinyEventsOutbox();
modelBuilder.UseTinyEventsOutbox("public.TinyOutbox");
```

Tests:

- model maps default table
- model maps custom table
- required columns and lengths match provider expectations
- EF Core can create database schema in PostgreSQL test container

Commit:

```text
Add PostgreSQL EF Core outbox mapping
```

### Slice 8: EF Core Writer

Goal: publish through caller-owned `DbContext` changes.

Work:

- Add `TinyPostgreSqlEfCoreOutboxWriter<TDbContext>`.
- Add provider options.
- Register writer through provider extension.

Tests:

- writer rejects null message
- writer adds outbox entity to DbContext
- caller `SaveChangesAsync` persists outbox row
- business row and outbox row commit together in one save

Commit:

```text
Add PostgreSQL EF Core outbox writer
```

### Slice 9: EF Core Store

Goal: support worker processing from EF Core provider.

Work:

- Add `TinyPostgreSqlEfCoreOutboxStore<TDbContext>`.
- Reuse PostgreSQL claim/mark SQL shape.
- Open the underlying relational connection when needed.
- Attach current EF transaction if one exists.

Tests:

- mirror ADO.NET store tests where possible
- claim pending
- skip future scheduled
- reclaim expired processing
- mark processed by owning worker
- mark failed by owning worker

Commit:

```text
Add PostgreSQL EF Core worker store
```

### Slice 10: Dependency Injection

Goal: expose provider registration APIs.

Work:

- Add:

```csharp
services.UsePostgreSqlAdoNetOutbox(...)
services.UsePostgreSqlEntityFrameworkCoreOutbox<TDbContext>(...)
```

- Register:
  - TinyEvents core services
  - generated contributions
  - provider writer
  - provider store
  - provider options

Tests:

- ADO.NET registration resolves `ITinyOutboxWriter`
- ADO.NET registration resolves `ITinyOutboxStore`
- EF Core registration resolves `ITinyOutboxWriter`
- EF Core registration resolves `ITinyOutboxStore`
- generated contributions are applied once

Commit:

```text
Add PostgreSQL provider registration
```

### Slice 11: End-To-End PostgreSQL Runtime Tests

Goal: prove the provider with real PostgreSQL.

Work:

- Add PostgreSQL Testcontainers fixture.
- Add shared PostgreSQL integration settings.
- Use Docker PostgreSQL in CI.

Tests:

- publish event through EF Core, process it, mark processed
- publish event through ADO.NET, process it, mark processed
- consumer failure marks failed or retry state
- multiple workers do not process the same active message
- expired claims can be retried

Commit:

```text
Add PostgreSQL end-to-end runtime tests
```

### Slice 12: Docker Compose And Samples

Goal: make local usage easy.

Work:

- Add PostgreSQL service to root `docker-compose.yml`.
- Add either:
  - PostgreSQL EF Core sample, or
  - extend package smoke sample to include PostgreSQL
- Keep sample small and operational.

Tests:

- sample smoke can run against local PostgreSQL
- package smoke covers new provider packages if feasible

Commit:

```text
Add PostgreSQL local run support
```

### Slice 13: Documentation

Goal: document PostgreSQL without overselling stability.

Work:

- Update README provider list.
- Add:
  - `docs/postgresql-ef-core.md`
  - `docs/postgresql-ado-net.md`
- Update:
  - `docs/schema-and-migrations.md`
  - `docs/workers.md`
  - `docs/roadmap.md`
  - `docs/README.md`
  - `samples/README.md`
- Document `FOR UPDATE SKIP LOCKED` as the provider claiming mechanism.

Tests:

- None beyond normal docs review.

Commit:

```text
Document PostgreSQL providers
```

### Slice 14: Package And CI Review

Goal: prepare PostgreSQL packages for alpha publishing.

Work:

- Confirm package metadata.
- Confirm package contents:
  - README
  - schema scripts
  - dependencies
- Update CI to restore/build/test all new projects.
- Decide whether PostgreSQL integration tests require Docker in CI or stay opt-in initially.

Tests:

- `dotnet test`
- `dotnet pack`
- inspect nupkg contents

Commit:

```text
Prepare PostgreSQL provider packages
```

## Risks

- PostgreSQL timestamp handling must be consistent with `DateTimeOffset`.
- Npgsql maps `timestamp with time zone` carefully; tests should prove round-tripping.
- Identifier quoting must be correct for default and custom table names.
- `FOR UPDATE SKIP LOCKED` must be inside one atomic claim operation.
- EF Core provider should not accidentally create SQL Server-shaped schema assumptions.
- ADO.NET provider should not own caller transactions.
- Provider registration should not duplicate core registration behavior.
- CI may need Docker-enabled PostgreSQL tests.

## Open Questions

### Do We Implement ADO.NET Or EF Core First?

Recommended: ADO.NET first.

Reason: ADO.NET makes the SQL and transaction boundaries explicit. Once claiming is correct there, EF Core can reuse the same SQL shape for worker operations.

### Do We Share SQL Between EF Core And ADO.NET?

Not initially.

Reason: SQL Server already has separate provider-local SQL classes. Mirroring that shape keeps the first PostgreSQL implementation easy to review. If duplication becomes painful after both providers are complete, extract provider-local shared helpers in a later cleanup slice.

### Do We Use Quoted PascalCase Or Lower Snake Case?

Recommended initial answer: quoted PascalCase.

Reason: it matches the existing `TinyOutboxMessage` shape and keeps EF Core and ADO.NET column mapping aligned with SQL Server. We can revisit lower snake case only if PostgreSQL ergonomics become more important than cross-provider symmetry.

### Do We Add A Shared Provider Test Contract?

Not at first.

Reason: shared contract tests can be useful after PostgreSQL exists. Before then, they may force abstraction too early. Start with clear provider-specific tests. Extract shared behavior tests later if the repetition becomes meaningful.

## Definition Of Done

PostgreSQL support is ready for an alpha package when:

- ADO.NET publishing works inside caller-owned transactions.
- EF Core publishing works inside caller-owned `DbContext` saves.
- Worker claiming is atomic with `FOR UPDATE SKIP LOCKED`.
- Mark processed and failed guard on current worker ownership.
- Expired processing claims can be reclaimed.
- Active processing claims are skipped by other workers.
- PostgreSQL schema script is packaged.
- EF Core mapping can create the schema.
- Integration tests pass against real PostgreSQL.
- Docs explain setup, schema, worker semantics, and limitations.
- Package metadata and contents are reviewed.
