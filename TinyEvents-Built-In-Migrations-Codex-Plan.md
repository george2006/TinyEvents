# TinyEvents Built-In Migrations — Codex Implementation Plan

Status: Implemented and acceptance-verified through MIG-25
Target: Next TinyEvents alpha
Working mode: Small reviewable slices, one slice at a time

---

## 0. Implementation checkpoint — 2026-07-29

Branch:

```text
feature/migrations
```

The first SQL Server implementation run completed MIG-2 through MIG-10 as nine
reviewed and committed slices:

| Slice | Commit | Established contract |
|---|---|---|
| MIG-2 | `8838f20` | Proved that migration source can compile into an existing provider assembly without a migration DLL or package dependency. |
| MIG-3 | `4bf9e8b` | Added the validated immutable `TinyEventsMigration` model. |
| MIG-4 | `ff0cea9` | Added the ordered, contiguous, immutable migration catalog. |
| MIG-5 | `60f7310` | Added the applied-history model and UTC timestamp invariant. |
| MIG-6 | `7160147` | Added the strict versioned SHA-256 checksum contract and fixed vectors. |
| MIG-7 | `9608009` | Added migration planning for empty, partial, current, incompatible, and incoherent history. |
| MIG-8 | `331f4c4` | Added transactional SQL Server schema/history bootstrap, reads, writes, table checks, and `TimeProvider` timestamps. |
| MIG-9 | `79f7e56` | Added deterministic session-scoped SQL Server locking, timeout, cancellation cleanup, explicit release, and concurrency tests. |
| MIG-10 | `b935d33` | Added authoritative SQL Server migration 001 shared by ADO.NET and EF Core. |

## 0.1 Final implementation checkpoint — 2026-08-15

The remaining architecture was implemented as reviewed slices:

| Slice | Commit | Established contract |
|---|---|---|
| MIG-11 | `f3a69ef` | Added SQL Server migration orchestration, transactional per-migration execution, strict planning, baselining, and retry behavior. |
| MIG-12 | `c72b9ba` | Connected the SQL Server ADO.NET provider through its worker connection factory with dedicated connection ownership. |
| MIG-13 | `ede86f5` | Connected the SQL Server EF Core provider through its scoped `DbContext` connection. |
| MIG-14 | `0ec78a3` | Added transactional PostgreSQL schema/history bootstrap and history behavior. |
| MIG-15 | `083866c` | Added deterministic PostgreSQL advisory locking, timeout, cancellation, and concurrency behavior. |
| MIG-16 | `c315bc9` | Added the authoritative PostgreSQL migration 001 shared by ADO.NET and EF Core. |
| Test boundary | `1264a47`, `e8d48aa` | Split PostgreSQL and SQL Server integration tests into provider-specific projects without duplicating shared fixtures. |
| MIG-17 | `c9074b7` | Added PostgreSQL migration orchestration with parity across planning, baselining, retry, and concurrency. |
| MIG-18 | `8359454` | Connected the PostgreSQL ADO.NET provider with dedicated connection ownership. |
| MIG-19 | `c8c8aff` | Connected the PostgreSQL EF Core provider with scoped `DbContext` connection ownership. |
| MIG-20 | `eaaafeb` | Added public `ITinyEventsMigrator` and `MigrateTinyEventsAsync` host entry points. |
| MIG-21 | `78effb4` | Froze the stable migration logging catalogue. |
| MIG-22 | `b68309d` | Added safe structured migration logging for start, apply, current, completion, and failure outcomes. |
| MIG-23 | `b57448b` | Documented the explicit four-provider migration workflow, alpha upgrade behavior, and non-goals. |
| MIG-24 | `fa39a74` | Added package-shape and clean-cache smoke gates plus runtime calls through all four packaged providers. |

MIG-25 acceptance completed successfully:

- Release solution build: zero warnings and zero errors;
- complete suite with both Testcontainers switches enabled: 493 passed, zero failed, zero skipped;
- SQL Server and PostgreSQL concurrent migration tests executed against real databases;
- package smoke: six expected packages, no migration package or DLL, clean isolated NuGet cache, runtime success through all four providers, and a real published `alpha.2` to local-package baseline upgrade on SQL Server and PostgreSQL;
- public API audit: only `ITinyEventsMigrator` and `TinyEventsMigrationServiceProviderExtensions` were added;
- documentation link audit: all local links in 29 tracked Markdown files resolved;
- branch scope and `git diff --check`: clean.

### Current implementation shape

Common migration engine source lives in:

```text
src/Shared/TinyEvents.Migrations/
```

SQL Server migration source lives in:

```text
src/Shared/TinyEvents.SqlServer.Migrations/
```

The common and SQL Server sources are compiled directly into both SQL Server
provider assemblies. No new runtime assembly or NuGet package exists.

SQL Server migration 001 is now the authoritative outbox schema source:

- the public ADO.NET schema helper delegates to it;
- the packed default SQL asset is checked against it;
- the EF Core model uses the same primary-key and index names;
- custom outboxes derive collision-safe object names;
- identifier limits fail before database work;
- migration source folders use forced LF checkout rules so strict checksums are
  stable across Windows and Unix.

### Verified baseline

At the end of MIG-10:

```text
SQL Server ADO.NET tests:       119 passed
SQL Server EF Core tests:       21 passed
SQL Server integration tests:   20 passed, 0 skipped
Full Release solution build:    succeeded, 0 warnings, 0 errors
git diff --check:               passed
```

The SQL Server integration suite was executed against the real Testcontainers
SQL Server image with Docker running.

### Important implementation findings

1. `Microsoft.Data.SqlClient` reports cancellation of a waiting
   `sp_getapplock` command as `SqlException`. The SQL Server lock translates
   this to caller cancellation only when the caller token is canceled and
   attempts release cleanup in case acquisition raced cancellation.
2. `sp_releaseapplock` may raise a provider exception directly when the current
   session does not own the lock. Release failures deliberately escape.
3. Compiling identical internal shared types into both SQL Server providers
   makes those types ambiguous to a test assembly that is a friend of both
   assemblies. The combined SQL Server integration project did not require EF
   Core internals, so its unused EF Core `InternalsVisibleTo` grant was removed.
4. The strict checksum contract makes source line endings a compatibility
   concern. Narrow `.gitattributes` rules now force LF only for shared migration
   source folders.

### Tomorrow: start with MIG-11

Implement SQL Server migrator orchestration only:

1. Open one migration-dedicated connection through an internal connection
   boundary suitable for the later ADO.NET and EF Core adapters.
2. Acquire the session-scoped migration lock before any schema, history, or
   alpha-baseline check.
3. Ensure the resolved schema in its dedicated bootstrap transaction.
4. Record whether history existed before this execution.
5. Ensure history in its dedicated bootstrap transaction.
6. If history was absent and the exact configured outbox base table exists,
   record migration 001 as the narrow alpha baseline without executing its SQL.
7. Read history and create a plan.
8. Execute each pending migration in its own transaction and append its history
   row in the same transaction.
9. Preserve earlier committed migrations if a later migration fails.
10. Re-read final history, require a current plan, and explicitly release the
    lock before reporting success.
11. Dispose the migration connection after lock release or failure.

MIG-11 must cover:

- fresh installation;
- current-schema no-op;
- alpha baseline;
- migration/history atomicity;
- partial progress and resume;
- final-history verification;
- lock held across bootstrap and all per-migration commits;
- release failure preventing successful completion;
- cancellation and coherent cleanup.

Do not begin adapter registration, provider exclusivity, or the public
`MigrateTinyEventsAsync` API in MIG-11. Those remain MIG-12, MIG-13, and MIG-20.

Before starting tomorrow, run:

```powershell
git switch feature/migrations
git status --short --branch
docker info
$env:TINYEVENTS_RUN_SQLSERVER_TESTS = "true"
dotnet test tests/TinyEvents.SqlServer.Tests/TinyEvents.SqlServer.Tests.csproj --configuration Release
```

Preserve the existing unrelated working-tree changes. Do not commit or modify
them as part of the migrations feature.

---

## 1. Objective

Add a small, explicit, forward-only migration engine that allows TinyEvents to create and evolve its own database schema safely.

The design must feel like a classic Unix tool:

- explicit;
- predictable;
- composable;
- boring in the best sense;
- easy to understand from type and method names;
- free from hidden startup behavior;
- small enough that every abstraction can be justified in one sentence.

TinyEvents manages only the TinyEvents schema. It is not a general-purpose migration framework.

---

## 2. User-facing lifecycle

Migrations are invoked explicitly before the host starts:

```csharp
var host = builder.Build();

await host.Services.MigrateTinyEventsAsync();

await host.RunAsync();
```

This must work for:

- ASP.NET Core applications;
- worker-only hosts;
- console migration tools;
- test hosts.

The hosted worker must not apply migrations automatically.

The provider registers the migration implementation. The common TinyEvents package exposes the entry point.

`MigrateTinyEventsAsync` may be called on the root service provider. It creates
and asynchronously disposes one dependency-injection scope for the complete
migration operation, then resolves the provider migrator from that scope.

---

## 3. Public API

The intended public entry point is:

```csharp
public static Task MigrateTinyEventsAsync(
    this IServiceProvider services,
    CancellationToken cancellationToken = default);
```

Normal completion means the configured TinyEvents schema is current. Failure
and cancellation are communicated through exceptions. Operational details such
as previous version, target version, and applied count belong in structured
logs rather than a public result model.

The provider integration contract is:

```csharp
public interface ITinyEventsMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken);
}
```

This interface exists because provider packages must register their own migrator implementation while the common `TinyEvents` assembly owns the public entry point.

`ITinyEventsMigrator` is registered as a scoped service. The public extension
does not resolve it directly from the root provider and does not require callers
to create a scope.

Do not add any other public migration contracts in v1 unless implementation proves one is unavoidable.

---

## 4. Coding style and working agreement

### 4.1 Style

Code must read from top to bottom like a short story.

Prefer:

- explicit control flow;
- intention-revealing names;
- one responsibility per type;
- small methods;
- early guards when they improve reading;
- blank lines between separate decisions;
- direct provider-specific code;
- source-generated logging only when it makes the calling code clearer;
- simple immutable models;
- internal implementation types;
- tests that describe observable contracts.

Avoid:

- clever pattern matching when a plain null check reads better;
- compressed expressions;
- generic abstractions created only to remove duplication;
- framework-like extension points;
- hidden service resolution;
- large inheritance hierarchies;
- magic conventions;
- speculative behavior;
- abstractions whose mission cannot be explained in one sentence.

Small duplication is preferable to an abstraction that hides real provider differences.

### 4.2 Slice workflow

For every slice:

1. Read all repository Markdown files first.
2. Inspect the current implementation before proposing changes.
3. Explain the problem and the exact contract of the slice.
4. Touch as few files as possible.
5. Prefer a 5–10 minute review scope.
6. Do not modify unrelated files.
7. Preserve all uncommitted user work.
8. Run focused tests first.
9. Run the relevant full project suite.
10. Run `git diff --check`.
11. Report:
   - findings;
   - exact files changed;
   - behavior established;
   - tests executed;
   - public API impact;
   - provider impact;
   - non-goals preserved.
12. Stop for review.
13. Do not commit or push until explicitly asked.

If a slice reveals that an abstraction is unnecessary, remove it rather than defending the original plan.

---

## 5. Non-goals for v1

Do not implement:

- down migrations;
- automatic rollback of already committed migrations;
- schema repair;
- deep schema introspection;
- user-defined migrations;
- a migration DSL;
- a public generic migration framework;
- provider-neutral SQL;
- a large exception hierarchy;
- migration modes such as `Apply`, `Validate`, or `Disabled`;
- migration execution during service registration;
- migration execution during dependency resolution;
- migration execution hidden inside the hosted worker;
- database availability probing before explicit migration execution;
- additional public NuGet packages;
- additional runtime assemblies;
- IL merging or post-build assembly rewriting.

Existing alpha installations receive one narrow compatibility rule: when the
migration history table does not exist but the configured TinyEvents outbox
table does exist, migration 001 is recorded as applied without executing its
SQL. This is an existence check, not schema validation.

If a user has manually altered the schema, that is outside the v1 compatibility guarantee.

---

## 6. Migration checksum contract

Every migration has a required SHA-256 checksum. The checksum detects when a
migration already recorded as applied is later changed in provider code.

The canonical checksum input is UTF-8 encoded in this exact order, with a zero
byte between fields:

```text
TinyEvents.Migrations.Checksum.v1
<zero byte>
<version as invariant decimal>
<zero byte>
<name exactly as stored>
<zero byte>
<SQL exactly as compiled>
```

TinyEvents stores the complete SHA-256 digest as 64 uppercase hexadecimal
characters.

The checksum is intentionally strict:

- version changes affect it;
- name changes, including case, affect it;
- SQL whitespace changes affect it;
- SQL formatting changes affect it;
- SQL line-ending changes affect it;
- SQL comments change it.

TinyEvents performs no trimming, line-ending conversion, whitespace
normalization, SQL parsing, or provider-specific canonicalization before
hashing. Released migration source, including its formatting and line endings,
is immutable. A schema change or cleanup belongs in a new migration.

Migration names and SQL must not contain the zero character. Rejecting it keeps
the field-separated checksum input unambiguous.

The domain marker, field order, separators, UTF-8 encoding, invariant version
format, exact-string rules, algorithm, and uppercase hexadecimal output are
compatibility contracts. Fixed-vector tests must hard-code expected checksums
instead of calculating expected values through the production implementation.

The alpha-baseline path records the current migration 001 checksum even though
it does not execute migration 001 SQL.

On every run, the planner compares all applied migration checksums with the
current catalog before returning a plan or executing pending work. A mismatch
fails clearly and performs no migration SQL or history rewrite.

The planner first compares the recorded name with the catalog name for that
version using ordinal comparison. A name mismatch fails clearly before checksum
comparison or pending work.

The history schema is new with this engine, so checksum is required and
non-null from its first version. TinyEvents does not support migration-history
rows without checksums.

---

## 7. Source-sharing and assembly layout

Migration implementation source is compiled directly into the existing provider assemblies.

No migration DLL is produced.

Suggested source folders:

```text
src/Shared/TinyEvents.Migrations/
src/Shared/TinyEvents.SqlServer.Migrations/
src/Shared/TinyEvents.PostgreSql.Migrations/
```

These are source-sharing folders, not class-library projects.

Provider projects include the relevant files using explicit MSBuild `Compile` items.

Example assembly contents:

```text
TinyEvents.SqlServer.AdoNet.dll
├── SQL Server ADO.NET provider
├── common migration engine source
├── SQL Server migration source
└── SQL Server ADO.NET migration connection adapter
```

```text
TinyEvents.SqlServer.EntityFrameworkCore.dll
├── SQL Server EF Core provider
├── common migration engine source
├── SQL Server migration source
└── SQL Server EF Core migration connection adapter
```

The same pattern applies to PostgreSQL.

All shared migration implementation types remain `internal`.

No shared migration type may appear in a public method signature.

### 7.1 Provider migration source ownership

Each database provider has one authoritative internal migration class per
version:

```text
src/Shared/TinyEvents.SqlServer.Migrations/
└── Migrations/
    └── SqlServerMigration001CreateOutbox.cs
```

```text
src/Shared/TinyEvents.PostgreSql.Migrations/
└── Migrations/
    └── PostgreSqlMigration001CreateOutbox.cs
```

The SQL Server class is compiled unchanged into both:

```text
TinyEvents.SqlServer.AdoNet
TinyEvents.SqlServer.EntityFrameworkCore
```

The PostgreSQL class is compiled unchanged into both:

```text
TinyEvents.PostgreSql.AdoNet
TinyEvents.PostgreSql.EntityFrameworkCore
```

ADO.NET and EF Core packages must not contain separate copies of migration SQL
maintained in different source files. Integration adapters supply the resolved
provider table identity; the shared provider migration class owns quoting and
the final provider SQL.

The existing public ADO.NET schema helpers remain compatibility APIs. Where
their contract matches migration 001, they should delegate to the same internal
provider migration SQL source rather than retain another generated-SQL copy.
This does not introduce a package dependency because the shared internal class
is compiled into the ADO.NET assembly.

The packed default `.sql` files remain user-facing package assets. Tests compare
their effective default schema contract with migration 001 so the static assets
cannot drift silently.

EF Core model mappings remain provider-package code because they describe an EF
model rather than migration execution. Provider integration tests prove that
their resulting logical outbox schema remains compatible with migration 001.

Changing an already released migration class is forbidden by the strict
checksum contract. Schema evolution adds a new provider migration class.

---

## 8. Abstractions and missions

Every abstraction must keep the mission below. If implementation work shows that a type does not earn its mission, remove or merge it.

| Abstraction | Mission |
|---|---|
| `TinyEventsMigration` | Describe one provider-specific schema change using version, name, SQL, and its deterministic checksum. |
| `AppliedTinyEventsMigration` | Represent one migration recorded in the database history. |
| `TinyEventsMigrationCatalog` | Describe all migrations known by the current provider code. |
| `TinyEventsMigrationPlan` | Describe the current version, target version, and pending migrations. |
| `TinyEventsMigrationPlanner` | Compare catalog and history and produce a plan. |
| `ITinyEventsMigrator` | Provide the common runtime entry point registered by a provider. |
| Provider migration history | Create, read, and append migration-history rows. |
| Provider migration lock | Ensure only one migrator executes against a schema at a time. |
| Provider migrator | Orchestrate connection, lock, history, planning, transactions, and execution. |
| Provider connection adapter | Open the migration connection using the package’s existing configuration model. |

---

## 9. Common engine concepts

### 9.1 `TinyEventsMigration`

Initial shape:

```csharp
internal sealed record TinyEventsMigration(
    long Version,
    string Name,
    string Sql);
```

Rules:

- version must be positive;
- name uses a numeric ID plus a stable explanation, such as
  `001_CreateTinyOutbox`;
- the numeric name prefix must equal the invariant decimal `Version` formatted
  with a minimum of three digits (`D3`), with no additional leading zeroes;
- the explanation uses stable ASCII letters and digits and begins with a
  letter;
- name must not be empty or whitespace;
- SQL must not be empty or whitespace;
- name and SQL must not contain the zero character;
- checksum is derived from version, name, and SQL using the v1 checksum contract;
- the object contains no execution behavior.

Examples:

```text
Version 1    → 001_CreateTinyOutbox
Version 2    → 002_AddDeliveryPriority
Version 1000 → 1000_ExampleFutureMigration
```

The numeric `Version` remains the ordering and compatibility key. The name is a
diagnostic identifier stored in history and used in logs and failures. Names do
not determine execution order.

Both the version and complete name are immutable after release. Renaming only
the explanatory part is still a migration checksum change and fails against
existing history.

The migration exposes its derived checksum:

```csharp
internal string Checksum { get; }
```

### 9.2 `AppliedTinyEventsMigration`

Initial shape:

```csharp
internal sealed record AppliedTinyEventsMigration(
    long Version,
    string Name,
    string Checksum,
    DateTimeOffset AppliedAtUtc);
```

It models history only. It does not decide compatibility.

### 9.3 `TinyEventsMigrationCatalog`

Responsibilities:

- expose an immutable ordered collection;
- require at least migration version 1;
- require the complete contiguous version sequence `1..N`;
- reject duplicate versions;
- reject duplicate names using ordinal comparison;
- reject non-positive versions;
- reject invalid migrations;
- expose the latest known version;
- contain no database logic.

### 9.4 `TinyEventsMigrationPlan`

Initial shape:

```csharp
internal sealed record TinyEventsMigrationPlan(
    long CurrentVersion,
    long TargetVersion,
    IReadOnlyList<TinyEventsMigration> PendingMigrations)
{
    public bool IsCurrent => PendingMigrations.Count == 0;
}
```

It represents a decision. It performs no work.

### 9.5 `TinyEventsMigrationPlanner`

Rules:

- empty history means all catalog migrations are pending;
- the catalog is a non-empty contiguous sequence starting at version 1;
- applied versions must form an exact contiguous prefix of the catalog;
- every applied name must exactly match the catalog name for its version;
- an applied version unknown to the catalog fails;
- a database version newer than the catalog fails before name/checksum
  compatibility checks or pending-work planning;
- missing intermediate versions fail;
- a fully applied catalog returns an empty plan;
- every already-applied migration checksum must match;
- the planner does not inspect physical schema objects.

The planner remains a separate type only if these rules make the policy easier to understand and test. If it becomes a trivial pass-through, merge it into the catalog or provider migrator.

---

## 10. Provider-specific implementation

### 10.1 SQL Server

Suggested source layout:

```text
src/Shared/TinyEvents.SqlServer.Migrations/
├── SqlServerTinyEventsMigrator.cs
├── SqlServerMigrationHistory.cs
├── SqlServerMigrationLock.cs
├── SqlServerMigrationCatalog.cs
├── ISqlServerMigrationConnectionFactory.cs
└── Migrations/
    └── SqlServerMigration001CreateOutbox.cs
```

Responsibilities:

- use `sp_getapplock` for exclusive migration locking;
- use SQL Server-specific history DDL;
- use SQL Server-specific migration SQL;
- execute each migration and its history insert in the same transaction;
- use parameterized commands for history writes;
- never log connection strings or SQL text.

Connection adapters:

```text
TinyEvents.SqlServer.AdoNet
→ SqlServerAdoNetMigrationConnectionFactory
→ delegates to the existing UseWorkerConnectionFactory(...) configuration
```

```text
TinyEvents.SqlServer.EntityFrameworkCore
→ SqlServerEfCoreMigrationConnectionFactory<TDbContext>
```

### 10.2 PostgreSQL

Suggested source layout:

```text
src/Shared/TinyEvents.PostgreSql.Migrations/
├── PostgreSqlTinyEventsMigrator.cs
├── PostgreSqlMigrationHistory.cs
├── PostgreSqlMigrationLock.cs
├── PostgreSqlMigrationCatalog.cs
├── IPostgreSqlMigrationConnectionFactory.cs
└── Migrations/
    └── PostgreSqlMigration001CreateOutbox.cs
```

Responsibilities:

- use a PostgreSQL advisory lock;
- derive the lock key deterministically from the TinyEvents schema identity;
- use PostgreSQL-specific history DDL;
- use PostgreSQL-specific migration SQL;
- execute each migration and its history insert in the same transaction;
- use parameterized commands for history writes;
- never log connection strings or SQL text.

Connection adapters:

```text
TinyEvents.PostgreSql.AdoNet
→ PostgreSqlAdoNetMigrationConnectionFactory
→ delegates to the existing UseWorkerConnectionFactory(...) configuration
```

```text
TinyEvents.PostgreSql.EntityFrameworkCore
→ PostgreSqlEfCoreMigrationConnectionFactory<TDbContext>
```

---

## 11. Migration history

The history table is migration infrastructure, not migration 001.

### 11.1 History table identity

The history table is derived from the configured outbox table. It uses the
same resolved schema and appends `Migrations` to the outbox table component.

Examples:

```text
dbo.TinyOutbox
→ dbo.TinyOutboxMigrations

app.MyOutbox
→ app.MyOutboxMigrations

public.TinyOutbox
→ public.TinyOutboxMigrations
```

Provider code must quote the derived identifier using the provider's existing
identifier rules.

The history table name is not separately configurable in v1. Deriving it keeps
each configured outbox independent without adding another public option that
could point an outbox at unrelated migration history.

Do not truncate the derived name and do not silently add a hash. If appending
`Migrations` exceeds the provider's identifier limit, explicit migration
execution fails with a clear configuration error before creating or changing
migration infrastructure.

Provider tests must cover:

- the default outbox name;
- a schema-qualified custom outbox name;
- provider-specific quoting;
- rejection when the derived identifier exceeds the provider limit;
- two outbox tables in the same schema deriving different history tables.

The normalized, derived history-table identity is also part of the provider
migration-lock identity.

### 11.2 Schema ownership

TinyEvents owns its outbox table, derived migration-history table, constraints,
and indexes. It ensures the containing database schema exists, but it does not
claim exclusive ownership of that schema because application objects may share
it.

The resolved schema is:

```text
SQL Server default → dbo
PostgreSQL default → public
custom             → configured resolved schema
```

After acquiring the migration lock, the provider ensures the resolved schema
exists in its own bootstrap transaction before checking the alpha baseline or
creating migration history.

If the schema already exists, TinyEvents leaves it unchanged. If it is absent,
TinyEvents creates it using provider-specific quoted DDL. If creation or commit
fails, the transaction rolls back, no migration infrastructure or migration SQL
is executed, and the exception escapes.

TinyEvents never drops the containing schema, enumerates unrelated objects in
it, changes its owner, or treats it as exclusively owned. Database permissions
and schema authorization remain deployment concerns; insufficient permission
to create a missing schema fails clearly.

SQL Server and PostgreSQL follow the same create-if-missing lifecycle. The
existing PostgreSQL public ADO.NET schema helper retains its compatible
create-schema behavior.

### 11.3 Database object names

Provider DDL derives constraint and index names from the configured outbox or
history table instead of using names that collide across custom outboxes.

Outbox migration 001 preserves the existing default names and generalizes them:

```text
PK_<OutboxTable>
IX_<OutboxTable>_Pending
IX_<OutboxTable>_ExpiredProcessing
IX_<OutboxTable>_ClaimedBy
```

The history primary-key name is:

```text
PK_<DerivedHistoryTable>
```

Names use the parsed table component, not the schema-qualified SQL text.
Provider-specific quoting is applied only after derivation.

Every derived database-object name is validated against the provider identifier
limit before any migration infrastructure or SQL is executed. TinyEvents does
not truncate names, accept server-side truncation, or silently hash them.

Tests cover default-name compatibility, two custom outboxes in one schema,
provider quoting, and rejection of every derived name that exceeds a provider
limit.

### 11.4 Migration lock ownership

TinyEvents uses one migration lock per derived history table.

The lock and history table represent the same migration ownership boundary:

```text
configured outbox
→ derived history table
→ migration lock
```

Two migrators targeting the same derived history table must therefore calculate
the same lock identity and serialize. Migrators targeting different derived
history tables may execute concurrently, including when their outbox tables are
in the same database schema.

This is preferred over:

- one database-wide lock, which would serialize unrelated TinyEvents outboxes;
- one schema-wide lock, which would still serialize unrelated outboxes in the
  same schema.

The lock identity is based only on:

- a versioned TinyEvents lock-domain marker;
- the provider;
- the resolved schema;
- the derived history-table name.

It must not contain a connection string, credentials, host name, process ID,
worker ID, assembly version, or another process-local value. It must not use
`string.GetHashCode()`, because that value is not a stable cross-process
compatibility contract.

The canonical lock input is UTF-8 encoded in this order, with a zero byte
between fields:

```text
TinyEvents.Migrations.Lock.v1
<zero byte>
<provider>
<zero byte>
<resolved schema>
<zero byte>
<derived history-table name>
```

Provider is the stable lowercase value `sqlserver` or `postgresql`.

SQL Server schema and table fields are normalized with invariant uppercase
before encoding. This ensures differently cased configuration values calculate
the same lock in the common case-insensitive SQL Server configuration. The
tradeoff is harmless extra serialization if a case-sensitive SQL Server
database contains identifiers that differ only by case.

PostgreSQL schema and table fields preserve their parsed case because TinyEvents
uses quoted, case-sensitive PostgreSQL identifiers.

TinyEvents hashes the canonical UTF-8 bytes with SHA-256. This shared algorithm
keeps lock calculation bounded, deterministic, Unicode-safe, and independent of
the provider lock API.

SQL Server uses this `sp_getapplock` resource:

```text
TinyEvents.Migrations.Lock.v1:<uppercase SHA-256 hexadecimal digest>
```

PostgreSQL interprets the first eight SHA-256 bytes as a signed 64-bit integer
in big-endian byte order and uses that value as the advisory-lock key.

The algorithm, field order, separators, case rules, hexadecimal casing, and
PostgreSQL byte order are compatibility contracts. Both providers require
fixed-vector tests that use hard-coded expected results rather than calculating
the expected value with the production implementation.

The provider APIs expose different final lock representations, but both use the
same canonical-input and SHA-256 rules.

The lock is required because migration execution is a read-decide-write
operation. A transaction makes one migration and its history insert atomic, but
does not prevent two application instances from reading the same current
version and both deciding to apply the same pending migration. The lock
serializes history inspection, alpha-baseline detection, planning, migration
execution, and history updates as one ownership period.

The provider lock must be acquired before checking for or creating migration
history and before checking whether migration 001 qualifies for the alpha
baseline.

### 11.5 Lock waiting and timeout

Migration lock acquisition waits for at most one minute and always honors the
`CancellationToken` passed to `MigrateTinyEventsAsync`.

The one-minute duration is a built-in v1 constant. It is not a public option.
This prevents indefinite deployment hangs without adding another configuration
surface.

Outcomes are distinct:

- lock acquired: migration continues;
- caller cancellation: `OperationCanceledException` escapes;
- one-minute wait expires: TinyEvents throws `TimeoutException`;
- provider lock error or deadlock outcome: the provider exception or a clear
  `InvalidOperationException` escapes; it is not mislabeled as a timeout.

The stable timeout message is:

```text
Timed out after 00:01:00 waiting for the TinyEvents migration lock for '{Schema}.{HistoryTable}'.
```

SQL Server passes the fixed timeout to `sp_getapplock` and interprets its return
code explicitly.

PostgreSQL uses cancellation-aware advisory-lock acquisition with an internal
one-minute timeout token. It distinguishes timeout expiration from cancellation
of the caller token.

If acquisition completes at the same time cancellation or timeout is observed,
connection and lock cleanup must still release any session lock that may have
been acquired.

Tests may supply a shorter timeout through an internal provider test seam. The
production timeout remains fixed and there is no public migration-lock options
type.

### 11.6 Lock release

The provider explicitly releases the session-scoped migration lock after final
history inspection and successful migration completion.

Migration success is returned only after explicit lock release succeeds. If
release fails, the failure escapes and the migration call does not report
success. Connection disposal remains the final fallback for releasing a
session-scoped database lock.

Already committed migrations remain applied when release fails. TinyEvents does
not retry the complete migration call internally. A host, deployment system, or
operator may retry; the next call reads committed history and normally returns
successfully without applying work.

Lock release failure must not rewrite history or cause rollback attempts for
previously committed migrations.

### 11.7 History lifecycle

After acquiring the provider lock, the migrator first checks whether the
history table already exists. It then ensures the history table exists.

If history did not exist before this execution but the configured TinyEvents
outbox table already exists, the migrator inserts the migration 001 history row
without executing migration 001 SQL. This is the alpha baseline path.

For this rule, “outbox table exists” means a real database base table at the
exact configured, resolved schema and table identity.

SQL Server checks parameterized system catalogs for a matching user table in
`sys.tables` and `sys.schemas`.

PostgreSQL checks parameterized system catalogs for a matching ordinary table
or partitioned table. PostgreSQL comparison preserves the parsed case used by
TinyEvents' quoted identifiers.

The following do not qualify for the alpha baseline:

- a view;
- a materialized view;
- a synonym;
- a sequence;
- another object type with the same name;
- a similarly named table in another schema;
- a PostgreSQL table whose quoted case does not match.

Existence checks use parameters for schema and table values. They do not build
catalog queries by interpolating configured identifiers.

TinyEvents does not inspect columns, types, indexes, constraints, or table
contents. Finding a real base table establishes only eligibility for the narrow
alpha baseline; it does not certify that a manually altered schema is valid.

Only migration 001 may be baselined. Later pending migrations are executed
normally.

Initial logical columns:

```text
Version       bigint, primary key
Name          provider-appropriate varchar/text
Checksum      provider-appropriate bounded text, exactly 64 uppercase hexadecimal characters
AppliedAtUtc  provider-appropriate UTC timestamp
```

### 11.8 Applied timestamp

The scoped provider migrator receives the existing registered `TimeProvider`
and obtains history timestamps through:

```csharp
timeProvider.GetUtcNow()
```

The value is captured immediately before adding the history insert parameter.
Normal migrations and the alpha baseline use the same rule.

The common applied-history model uses `DateTimeOffset`. SQL Server persists the
value as `datetimeoffset`; PostgreSQL persists the UTC instant as `timestamp
with time zone`.

`AppliedAtUtc` is audit metadata only. It does not affect migration ordering,
planning, lock identity, or checksums. TinyEvents does not compare timestamps
for monotonicity and does not use them to infer schema version.

Using `TimeProvider` gives both providers one time source and allows exact
timestamp assertions without relying on the machine or database clock.

Do not include in v1:

- duration;
- host name;
- worker ID;
- success flag;
- SQL content;
- library version.

A normal migration row is inserted only after the migration SQL completes
successfully and within the same transaction.

The alpha baseline row is the sole exception: it is inserted transactionally
after the outbox existence check, without executing migration 001 SQL.

---

## 12. Migration execution flow

```text
MigrateTinyEventsAsync
→ create async dependency-injection scope
→ resolve scoped ITinyEventsMigrator
→ open provider connection
→ acquire provider migration lock
→ ensure the resolved database schema exists transactionally
→ check whether migration history exists
→ if history is absent, check whether the configured outbox table exists
→ ensure history table exists
→ if history was absent and outbox exists, record migration 001 as the alpha baseline
→ read applied history
→ obtain provider catalog
→ create migration plan
→ for each pending migration
   → begin transaction
   → execute migration SQL
   → insert history row
   → commit
→ release lock
→ complete successfully
→ asynchronously dispose dependency-injection scope
```

The async scope is disposed when migration succeeds, fails, or is cancelled.
Migration and provider exceptions escape unchanged after cleanup. The public
extension does not convert failures into status values.

Cancellation must:

- stop waiting for a lock;
- stop command execution where supported;
- escape to the caller;
- never be translated into another failure shape;
- leave uncommitted migration work rolled back.

### 12.1 Scope and connection ownership

The public `MigrateTinyEventsAsync` extension owns the migration operation's
dependency-injection scope.

The scoped provider migrator owns:

- the provider migration lock lifetime;
- each transaction it begins;
- each command and reader it creates.

For ADO.NET providers, explicit migrations reuse the connection delegate already
configured through `UseWorkerConnectionFactory(...)`. TinyEvents does not add a
separate public migration-connection delegate.

Each invocation of the existing delegate returns a connection dedicated to that
worker operation or migration operation. During migration, TinyEvents opens the
returned connection when necessary and asynchronously disposes it when
migration completes, fails, or is cancelled. It does not use the application's
current publishing transaction.

An ADO.NET application that calls `MigrateTinyEventsAsync` must configure
`UseWorkerConnectionFactory(...)` even if it does not run the hosted worker or
manually process outbox messages.

Missing ADO.NET connection-factory configuration fails only during explicit
migration execution or existing worker validation. Provider registration and
ordinary dependency resolution do not invoke the delegate or open a database
connection.

The existing missing-factory validation message and provider documentation must
state that `UseWorkerConnectionFactory(...)` is required for outbox claiming,
marking, and explicit TinyEvents migrations.

For EF Core providers, the scoped `DbContext` owns its relational connection.
The migration adapter may open that connection when necessary but does not
independently dispose the connection or the `DbContext`. Disposing the async
scope disposes the context and its owned resources.

Provider migrators must not create nested dependency-injection scopes. Scope
creation belongs only to the common public entry point.

### 12.2 Transaction boundaries and recovery

TinyEvents uses one database transaction per pending migration.

Within that transaction, the provider:

1. executes the migration SQL;
2. inserts the migration history row, including its checksum;
3. commits.

Migration SQL and its history insert must never use separate transactions. A
committed schema change without matching history, or matching history without
the schema change, would make future planning unsafe.

The provider migration lock remains held across the complete migration run,
including schema bootstrap, history inspection, alpha-baseline detection, and
every individual migration transaction through successful completion. The lock must be
session-scoped rather than transaction-scoped so committing one migration does
not release ownership before the next migration is planned or applied.

If several migrations are pending and a later migration fails:

- its transaction rolls back;
- it has no history row;
- earlier committed migrations remain applied;
- the call does not complete successfully;
- the exception escapes;
- the next explicit migration call resumes after the last committed version.

TinyEvents does not wrap the complete run in one transaction and does not roll
back migrations that committed successfully before a later failure.

History-table creation is migration infrastructure and uses its own bootstrap
transaction after the session-scoped migration lock is acquired:

```text
check whether history exists
→ if absent, begin history-bootstrap transaction
→ create history table
→ commit history-bootstrap transaction
```

If history already exists, no bootstrap transaction or create command is
needed. If history creation or commit fails, the bootstrap transaction rolls
back, no migration is planned or executed, and the exception escapes.

Both SQL Server and PostgreSQL implementations rely on their transactional DDL
behavior for this contract.

The alpha-baseline migration 001 history insert uses its own transaction. It
records the version, name, strict checksum, and application time atomically,
without executing migration 001 SQL.

There is an intentional recovery boundary between committed history bootstrap
and the alpha-baseline transaction. If the process stops between them, the next
call sees an existing empty history table and follows normal empty-history
planning. Migration 001 must therefore remain safe when the configured outbox
table already exists.

Cancellation or commit failure follows the same rule: the current uncommitted
transaction is disposed and rolled back, previously committed migrations remain
applied, and the call does not complete successfully.

### 12.3 Missing provider

Calling `MigrateTinyEventsAsync` without a registered TinyEvents database
provider throws `InvalidOperationException`.

The public extension checks for the scoped `ITinyEventsMigrator` and throws this
deliberate error instead of exposing the dependency-injection container's
generic missing-service message:

```text
No TinyEvents database provider is registered. Register exactly one TinyEvents database provider before calling MigrateTinyEventsAsync.
```

Explicit migration execution must never complete successfully when no provider
is registered. Doing so would hide a deployment or application configuration
error.

The async scope created by the public extension is still disposed before the
exception escapes.

---

## 13. Failure semantics

### Current schema

Complete successfully without applying migration SQL or adding history rows.

### Missing provider

Fail with the deliberate `InvalidOperationException` defined by the public
entry-point contract. Do not complete successfully.

### New installation

Create history infrastructure and apply all known migrations in order.

### Existing alpha installation

When migration history is absent and the configured outbox table exists:

- create the history table;
- record migration 001 as applied without running its SQL;
- execute any migrations after version 1 normally.

Do not inspect columns, indexes, constraints, or types. A manually altered
table is unsupported and may fail during later migrations or runtime use.

A non-table object with the configured name does not qualify as an existing
alpha installation.

If the history table already exists, even if it is empty, do not use the alpha
baseline rule. Normal history planning applies.

### Older managed schema

Apply pending migrations in order.

### Newer database schema

If the highest applied history version is greater than the latest version in
the current provider catalog, throw `InvalidOperationException`.

This protects application rollback scenarios. Older provider code cannot know
whether a newer migration changed schema semantics required by the older
runtime.

The stable message is:

```text
TinyEvents schema version {DatabaseVersion} is newer than version {ProviderVersion} supported by the registered provider. Upgrade the application or use a compatible database.
```

The migrator acquires the provider lock and reads history, but it does not:

- execute migration SQL;
- insert, update, or delete history;
- attempt down migration;
- complete successfully;

The exception escapes the explicit migration call after connection, lock, and
scope cleanup.

### Incoherent history

Fail clearly. Do not repair.

History is incoherent when its versions are not the exact prefix `1..N` of the
current provider catalog. TinyEvents does not skip a missing version, infer it
from later rows, insert a replacement row, or renumber history.

### Checksum mismatch

Fail clearly before executing pending migration SQL. Do not continue and do not
rewrite history.

### Migration command failure

Rollback the current transaction and escape.

Previously committed migrations remain applied.

The next migration call resumes from the last committed history version.

### Lock acquisition failure or timeout

Caller cancellation escapes as `OperationCanceledException`. Expiration of the
fixed one-minute wait throws `TimeoutException`. Other provider acquisition
failures escape without being relabeled.

Do not inspect history, baseline, or run migration SQL without successfully
acquiring the lock.

### Lock release failure

Do not return success. Let the release failure escape after connection cleanup.
Committed migrations and history remain applied. Recovery is a later explicit
call, not an internal TinyEvents retry.

### Manually altered physical schema

Outside v1 guarantees. The database may fail while applying or using the schema.

---

## 13.1 Rolling-deployment compatibility

The migration lock coordinates migrators only. It is not a maintenance lock and
does not block already-running publishers, workers, or direct
`ITinyOutboxProcessor` calls.

Built-in TinyEvents migrations must therefore remain compatible with supported
older application instances that may continue using the outbox during a rolling
deployment.

Migration design is expand-first:

- add new tables or indexes without changing existing runtime meaning;
- add columns only when older inserts and updates remain valid;
- use nullable columns or safe database defaults when old code does not provide
  a new value;
- preserve existing table and column names used by released providers;
- preserve existing column meanings and compatible data types;
- ensure older claim, mark, and insert SQL continues to work.

A built-in migration must not:

- drop or rename a runtime table or column still used by a supported release;
- make an existing nullable column required without a compatibility path;
- add a required column without a default that supports older writes;
- change stored status meanings while older workers may process rows;
- require publishers or workers to acquire the migration lock on every runtime
  operation.

Destructive cleanup requires a separately documented future compatibility
window or release procedure. It is not combined with the expand migration that
introduces its replacement.

Every new migration slice must state its rolling-deployment compatibility
argument and add focused tests proving previously released insert, claim, and
mark behavior remains valid where the migration affects those contracts.

Migration 001 is naturally compatible for a new installation. On the alpha
baseline path it executes no schema SQL, so existing alpha runtime behavior is
unchanged.

TinyEvents does not add an internal retry loop for migration failures. Hosts,
deployment systems, and operators own retry policy. A retry after another
migrator completed normally observes current history and completes successfully
without applying work.

---

## 14. Registration ownership

`AddTinyEventsWorker()` does not register a migrator.

The selected database provider registers the implementation:

```text
UseSqlServerAdoNetOutbox
→ ITinyEventsMigrator = SqlServerTinyEventsMigrator
```

```text
UseSqlServerEntityFrameworkCoreOutbox
→ ITinyEventsMigrator = SqlServerTinyEventsMigrator
```

```text
UsePostgreSqlAdoNetOutbox
→ ITinyEventsMigrator = PostgreSqlTinyEventsMigrator
```

```text
UsePostgreSqlEntityFrameworkCoreOutbox
→ ITinyEventsMigrator = PostgreSqlTinyEventsMigrator
```

### 14.1 Exactly one provider registration

Exactly one TinyEvents database-provider registration call is allowed per
`IServiceCollection`.

A second provider-registration call is a configuration bug and throws
immediately. This includes:

- registering a different database provider;
- registering a different integration for the same database;
- repeating the same provider-registration method.

Examples:

```csharp
services.UseSqlServerAdoNetOutbox(...);
services.UsePostgreSqlAdoNetOutbox(...); // throws
```

```csharp
services.UseSqlServerAdoNetOutbox(...);
services.UseSqlServerAdoNetOutbox(...); // throws
```

Repeated registration is not treated as idempotent because configuration
delegates, connection factories, `DbContext` types, and option values cannot be
compared reliably. It is not last-registration-wins because that can silently
combine one provider's options with another provider's writer, store, or
migrator.

Each provider registration method performs the exclusivity guard before:

- invoking its configuration delegate;
- calling `UseTinyEvents`;
- adding, replacing, or mutating any service descriptor.

If registration fails, the original service collection remains unchanged and
the originally registered provider remains usable.

The guard uses one internal core registration marker with a stable provider
identity:

```text
SqlServer.AdoNet
SqlServer.EntityFrameworkCore
PostgreSql.AdoNet
PostgreSql.EntityFrameworkCore
```

The marker and guard remain internal. They do not add a public provider model
or named-provider API.

The exception identifies both the existing and requested provider. Its message
must explain that TinyEvents supports one database provider per service
collection.

This rule applies only to database-provider registration. Repeated core
registration through `UseTinyEvents` keeps its existing contract, and
`AddTinyEventsWorker` remains independent.

Tests must cover:

- repeating each of the four provider methods;
- conflicting registration across provider methods;
- failure before the second configuration delegate runs;
- no service-descriptor mutation after a rejected registration;
- successful resolution of the original writer, store, options, and migrator
  after rejection.

---

## 15. Logging

Do not design the complete logging catalogue before the runner exists.

Initial required outcomes:

- migration started;
- migration applied;
- migration completed;
- migration failed;
- schema already current;
- waiting for migration lock, only if operationally useful.

Use stable event IDs and structured properties.

Safe properties may include:

- provider;
- schema;
- table;
- migration version;
- migration name;
- previous version;
- target version;
- applied count;
- duration.

Never log:

- connection strings;
- credentials;
- SQL;
- payloads;
- database command parameter values that may contain secrets.

Logging must be implemented in a dedicated component, not as helper methods inside the migrator.

### 15.1 Implemented-flow event inventory

MIG-21 fixes the smallest stable v1 catalogue from the completed runner. Event
IDs continue after the existing worker and processor ranges.

| Event ID | Event name | Level | Emission | Structured properties |
| --- | --- | --- | --- | --- |
| 1400 | `MigrationStarted` | Information | Once when an explicit provider migration call begins. | `Provider`, `Schema`, `Table` |
| 1401 | `MigrationApplied` | Information | Once after a migration SQL/history transaction commits, or after the migration-001 alpha baseline history transaction commits. | `Provider`, `Schema`, `Table`, `MigrationVersion`, `MigrationName`, `IsBaseline` |
| 1402 | `MigrationSchemaCurrent` | Information | Once when planning finds no pending migrations. | `Provider`, `Schema`, `Table`, `CurrentVersion`, `TargetVersion` |
| 1403 | `MigrationCompleted` | Information | Once after final history inspection proves the schema current. | `Provider`, `Schema`, `Table`, `PreviousVersion`, `TargetVersion`, `AppliedCount`, `ElapsedMilliseconds` |
| 1404 | `MigrationFailed` | Error | Once when an explicit provider migration call fails, including lock acquisition or release failure; the original exception still escapes. | `Provider`, `Schema`, `Table`, `ElapsedMilliseconds` |

`Provider` uses the stable lowercase values `sqlserver` and `postgresql`.
`Schema` and `Table` are the resolved exact identifiers already held by the
provider migrator. `PreviousVersion` is the coherent history version observed
before baseline or pending-migration execution. `AppliedCount` includes a
committed alpha-baseline row. `IsBaseline` distinguishes that row from executed
migration SQL without logging SQL or physical-schema details.

`MigrationCompleted` is emitted for every successful call. A current/no-op call
therefore emits started, schema-current, and completed. A call that applies
migrations emits started, one applied event per committed migration, and
completed. A failed call emits started and failed, plus any applied events for
earlier transactions that committed before the failure.

The failure event carries the exception through the logging API but never adds
connection strings, credentials, SQL, command text, parameters, payloads, or
provider exception detail as structured properties. Cancellation is not logged
as an error event because it is caller-directed control flow and still escapes
unchanged.

No lock-wait event is included in v1. Acquisition already has a bounded timeout,
and logging every uncontended acquisition would add noise. A future lock-wait
event requires evidence that a distinct operational signal is useful.

---

## 16. Test strategy

### Pure tests

Cover:

- migration validation;
- numeric-ID-plus-explanation name validation;
- version/name-prefix mismatch;
- catalog ordering and duplicate rejection;
- empty-catalog rejection;
- sparse-catalog rejection;
- duplicate-name rejection;
- planning from empty history;
- partial history;
- current history;
- newer schema;
- stable newer-schema error containing database and provider versions;
- newer-schema failure performs no migration SQL or history writes;
- unknown version;
- history gaps;
- strict checksum calculation;
- fixed checksum vectors;
- checksum mismatch behavior.

### Provider integration tests

Use Testcontainers.

Cover for both SQL Server and PostgreSQL:

- fresh installation;
- existing alpha base table records migration 001 without executing its SQL;
- missing configured schema is created transactionally;
- schema-bootstrap failure creates no migration objects;
- alpha baseline records the strict migration 001 checksum;
- a non-table object with the configured name does not trigger the alpha
  baseline;
- a table in another schema does not trigger the alpha baseline;
- PostgreSQL quoted-case mismatch does not trigger the alpha baseline;
- history creation;
- migration 001 application;
- history row persisted;
- second execution is a no-op;
- migration SQL and history row are transactional;
- absent history is created in its own bootstrap transaction;
- history-bootstrap failure executes no migration SQL;
- interruption between history bootstrap and alpha baseline recovers through
  normal empty-history planning;
- each pending migration uses a separate transaction;
- a later failure preserves earlier committed migrations;
- a later run resumes after the last committed migration;
- alpha-baseline history insertion is transactional;
- normal and alpha-baseline rows use `TimeProvider.GetUtcNow()`;
- exact applied timestamps can be tested with a controlled `TimeProvider`;
- concurrent migrators serialize correctly;
- cancellation while waiting for lock;
- fixed lock-wait timeout throws `TimeoutException`;
- timeout and caller cancellation remain distinguishable;
- lock-acquisition failure performs no history or migration work;
- lock-release failure prevents successful completion;
- retry after lock-release failure observes committed history;
- rolling-deployment compatibility with previously released runtime SQL;
- failure leaves coherent history;
- ADO.NET path;
- ADO.NET migration reuses `UseWorkerConnectionFactory(...)`;
- ADO.NET missing-factory failure is actionable and opens no connection;
- EF Core path;
- no extra migration assembly in package output.

The test infrastructure should reuse one database container per provider test assembly where practical.

---

## 17. Implementation slices

Each slice must remain independently reviewable.

### MIG-1 — Repository migration inventory

Status: analysis only.

Inspect:

- every repository Markdown file;
- current SQL Server schema scripts;
- current PostgreSQL schema scripts;
- EF Core mappings;
- ADO.NET writers and stores;
- provider option types;
- connection factories;
- DI registration methods;
- package layout;
- package-smoke workflow;
- Testcontainers fixtures;
- current public API and namespace conventions.

Return:

- exact current schema definitions;
- real SQL Server/PostgreSQL differences;
- connection ownership for all four provider packages;
- best location for the public entry point;
- exact registration seam;
- source-sharing MSBuild proposal;
- packaging risks;
- changes recommended to this plan.

Do not change files.

### MIG-2 — Prove source sharing in one pilot provider

Goal: validate packaging before building the engine.

Pilot: SQL Server ADO.NET.

Changes:

- add the common shared-source folder;
- add one trivial internal source-shared type;
- compile it into `TinyEvents.SqlServer.AdoNet`;
- pack the provider;
- prove no migration DLL or package dependency appears;
- verify Visual Studio and solution builds remain clean.

No migration behavior yet.

Stop for review.

### MIG-3 — Add `TinyEventsMigration`

Files should remain minimal.

Establish:

- version;
- numeric-ID-plus-explanation name;
- matching version prefix;
- SQL;
- constructor validation;
- immutability;
- pure tests.

No catalog, planner, database, or provider behavior.

Stop for review.

### MIG-4 — Add `TinyEventsMigrationCatalog`

Establish:

- immutable ordered migrations;
- non-empty catalog starting at version 1;
- contiguous `1..N` versions;
- positive versions;
- duplicate rejection;
- duplicate-name rejection;
- latest known version;
- focused pure tests.

Stop for review.

### MIG-5 — Add applied-history model

Add only `AppliedTinyEventsMigration` and its minimal invariants.

Do not add database access.

Stop for review.

### MIG-6 — Add strict migration checksums

Implement:

- the versioned, zero-separated checksum input;
- exact UTF-8 encoding;
- SHA-256;
- 64-character uppercase hexadecimal output;
- zero-character rejection in migration name and SQL;
- checksum exposure from `TinyEventsMigration`;
- fixed-vector tests;
- tests proving that version, name, whitespace, formatting, line endings, and
  comments affect the checksum.

Do not add SQL normalization or a public checksum abstraction.

### MIG-7 — Add migration planning

Implement:

- empty history;
- partial coherent history;
- current history;
- newer schema;
- unknown applied version;
- history gaps;
- history that does not start at version 1;
- applied-name mismatch;
- checksum mismatch before pending work.

Keep or remove `TinyEventsMigrationPlanner` based on whether the type improves the code.

Stop for review.

### MIG-8 — SQL Server history bootstrap

Implement only:

- history-table name derivation from the configured outbox table;
- SQL Server identifier-length validation;
- parameterized schema-existence check;
- transactional create-if-missing schema bootstrap;
- derived history constraint-name validation;
- SQL Server history table creation;
- transactional SQL Server history bootstrap;
- SQL Server history-table existence check;
- parameterized configured base-table existence check through SQL Server system
  catalogs;
- history reading;
- history append;
- `DateTimeOffset` history timestamps from `TimeProvider`;
- integration tests with Testcontainers.

Do not apply outbox migration yet.

Stop for review.

### MIG-9 — SQL Server migration lock

Implement only:

- `sp_getapplock`;
- deterministic resource name containing the normalized, derived history-table identity;
- one lock per derived history table;
- fixed-vector tests for the final identity encoding;
- fixed one-minute timeout;
- distinct cancellation, timeout, and provider-error behavior;
- internal shortened-timeout test seam;
- explicit release and release-failure behavior;
- real concurrency test.

Stop for review.

### MIG-10 — SQL Server migration 001

Implement:

- one provider-specific shared-source class containing the authoritative SQL;
- compile the class unchanged into both SQL Server provider assemblies;
- create current TinyEvents outbox schema;
- use the resolved schema ensured by orchestration;
- derive collision-safe constraint and index names;
- remain safe when the outbox already exists but history is empty;
- exact parity with current SQL Server schema contract;
- delegate the compatible public ADO.NET schema helper to the internal source;
- verify the packed default SQL asset and EF Core logical mapping remain
  compatible;
- focused SQL Server integration tests.

Do not yet expose the public API.

Stop for review.

### MIG-11 — SQL Server migrator orchestration

Implement:

- connection;
- lock;
- history;
- alpha baseline of migration 001 when history is absent and outbox exists;
- plan;
- transaction per migration;
- history insert in same transaction;
- session-scoped lock retained across migration commits;
- resume after the last committed migration;
- no-op on current schema;
- failure rollback;
- successful completion.

Stop for review.

### MIG-12 — SQL Server ADO.NET adapter and registration

Implement:

- ADO.NET migration connection adapter;
- reuse of the existing `UseWorkerConnectionFactory(...)` delegate;
- missing-factory migration validation and actionable message;
- migration-dedicated connection ownership and asynchronous disposal;
- provider DI registration;
- the internal provider-registration exclusivity guard;
- fail-before-mutation tests covering all four existing provider methods;
- internal migrator resolution;
- no public entry point yet.

Stop for review.

### MIG-13 — SQL Server EF Core adapter and registration

Implement:

- EF Core migration connection adapter;
- registration-exclusivity parity;
- correct provider validation;
- scoped `DbContext` connection ownership and disposal rules;
- reuse the same SQL Server migration source;
- no duplicate schema logic.

Stop for review.

### MIG-14 — PostgreSQL history bootstrap

Mirror the contract, not necessarily the code shape.

Use PostgreSQL-specific SQL for history creation, history existence, and the
configured outbox-table existence check.

Include history-table name derivation from the configured outbox table and
PostgreSQL identifier-length validation. PostgreSQL's identifier limit must be
enforced without relying on server-side truncation.

The PostgreSQL outbox existence check is parameterized and accepts only an
ordinary or partitioned table with the exact resolved schema and parsed case.

PostgreSQL history creation uses the same dedicated transactional-bootstrap
contract as SQL Server.

PostgreSQL history writes preserve the `TimeProvider` UTC instant through
`timestamp with time zone`.

Ensure the resolved PostgreSQL schema exists through the dedicated
create-if-missing bootstrap transaction and validate all derived history object
names before creating migration infrastructure.

Stop for review.

### MIG-15 — PostgreSQL advisory lock

Implement:

- deterministic advisory-lock key containing the normalized, derived history-table identity;
- one lock per derived history table;
- fixed-vector tests for the final identity encoding;
- exclusive acquisition;
- fixed one-minute timeout;
- distinct cancellation, timeout, and provider-error behavior;
- internal shortened-timeout test seam;
- explicit release and release-failure behavior;
- concurrency test.

Stop for review.

### MIG-16 — PostgreSQL migration 001

Implement:

- one provider-specific shared-source class containing the authoritative
  PostgreSQL SQL;
- compile the class unchanged into both PostgreSQL provider assemblies;
- use the resolved schema ensured by orchestration;
- preserve the public schema helper's compatible create-schema behavior;
- derive collision-safe constraint and index names;
- exact parity with current PostgreSQL schema contract;
- remain safe when the outbox already exists but history is empty;
- delegate the compatible public ADO.NET schema helper to the internal source;
- verify the packed default SQL asset and EF Core logical mapping remain
  compatible;
- integration tests.

Stop for review.

### MIG-17 — PostgreSQL migrator orchestration

Implement PostgreSQL-owned orchestration, including the same narrow alpha
baseline rule as SQL Server.

Duplication with SQL Server is acceptable when it keeps provider behavior explicit.

Match the transaction-per-migration, atomic history, session-lock, partial
progress, and resume contracts established by the SQL Server orchestration.

Stop for review.

### MIG-18 — PostgreSQL ADO.NET adapter and registration

Implement and test, including registration-exclusivity parity,
`UseWorkerConnectionFactory(...)` reuse, missing-factory migration validation,
and migration-dedicated connection ownership.

Stop for review.

### MIG-19 — PostgreSQL EF Core adapter and registration

Implement and test, including registration-exclusivity parity and scoped
`DbContext` connection ownership.

Stop for review.

### MIG-20 — Public migration entry point

Only after both providers work internally.

Add:

- `ITinyEventsMigrator`;
- `MigrateTinyEventsAsync`;
- creation and asynchronous disposal of one scope per migration call;
- scoped migrator resolution;
- deliberate missing-provider `InvalidOperationException`;
- stable, actionable missing-provider message;
- missing-provider scope-disposal test;
- tests from generic host and worker-only host.

Stop for review.

### MIG-21 — Logging inventory

Analysis only.

Define the smallest stable event catalogue based on the implemented flow.

No logging behavior changes yet.

### MIG-22 — Structured migration logging

Implement dedicated logging components.

Cover:

- started;
- applied;
- current/no-op;
- completed;
- failed;
- safe structured properties;
- no SQL or secrets.

Stop for review.

### MIG-23 — Documentation

Update:

- getting started;
- worker-only host example;
- SQL Server ADO.NET;
- SQL Server EF Core;
- PostgreSQL ADO.NET;
- PostgreSQL EF Core;
- alpha upgrade note;
- explicit non-goals;
- existing-alpha schema guidance.

Stop for review.

### MIG-24 — Packaging and package-smoke gate

Prove:

- no new migration DLL;
- no new public migration package;
- provider packages contain required compiled code;
- package-smoke consumers can call the public API;
- clean Visual Studio solution build;
- clean empty NuGet-cache restore/build.

Stop for review.

### MIG-25 — Full acceptance and alpha release preparation

Run:

- Release build;
- all unit tests;
- all SQL Server Testcontainers tests;
- all PostgreSQL Testcontainers tests;
- concurrent migration tests;
- package smoke;
- public API audit;
- docs link audit;
- branch scope review;
- `git diff --check`.

Prepare the next alpha only after review.

---

## 18. Feature acceptance criteria

The feature is complete when:

- a user can explicitly migrate before starting any `IHost`;
- the same API works for web, worker-only, console, and tests;
- SQL Server ADO.NET and EF Core use the same SQL Server migration source;
- PostgreSQL ADO.NET and EF Core use the same PostgreSQL migration source;
- SQL Server and PostgreSQL may evolve independently;
- fresh databases install correctly;
- managed older schemas upgrade correctly;
- current schemas are a no-op;
- newer schemas fail clearly;
- incoherent history fails clearly;
- concurrent startup is serialized safely;
- migration SQL and history insert are atomic per migration;
- cancellation escapes cleanly;
- no migration DLL or extra NuGet package exists;
- all implementation types are internal except the three intentional public API elements;
- documentation matches behavior;
- all real database tests pass.

---

## 19. Codex master prompt

```text
You are implementing built-in schema migrations for TinyEvents.

Before changing code, read every Markdown file in the repository and inspect the current SQL Server and PostgreSQL schema scripts, EF Core mappings, ADO.NET stores, dependency injection registrations, package layout, integration tests, coding guide, and public API conventions.

Use TinyEvents-Built-In-Migrations-Codex-Plan.md as the implementation source of truth.

The migration system must be small, explicit, forward-only, provider-owned, and readable like a classic Unix tool.

Core principles:

1. TinyEvents manages only its own tables, constraints, indexes, and migration history. It transactionally ensures the containing database schema exists but never drops it or claims exclusive ownership.
2. Migration execution is explicit through MigrateTinyEventsAsync.
3. No migrations run during service registration, dependency resolution, or hidden worker startup.
4. SQL Server and PostgreSQL own separate migration implementations and SQL.
5. EF Core and ADO.NET for the same database provider reuse provider migration source code.
6. Migration source is compiled into the existing public provider assemblies.
7. Do not introduce migration DLLs or public NuGet packages.
8. All engine and provider implementation types remain internal.
9. Prefer small duplication over abstractions that obscure provider differences.
10. Every abstraction must have one clearly explainable responsibility.
11. No Down migrations, schema repair, general automatic baselining, user migration framework, or migration DSL in v1. Preserve only the documented migration-001 alpha baseline.
12. Every migration uses the documented strict SHA-256 checksum contract. Released migration content is immutable.
13. Do not alter or commit unrelated user files.
14. Implement one approved slice at a time.
15. Stop after each slice for review.
16. Do not commit or push unless explicitly asked.

Code style:

- code reads top-to-bottom like a story;
- explicit control flow;
- calm and boring implementation;
- intention-revealing names;
- blank lines between separate decisions;
- no clever syntax when simple syntax is clearer;
- no speculative abstractions;
- dedicated logging components;
- small focused tests;
- no hidden magic.

For the requested slice:

1. Inspect the real repository first.
2. Explain findings and any improvement to the plan.
3. State exact scope and non-goals.
4. Change only the minimum files.
5. Run focused tests, relevant full tests, and git diff --check.
6. Report exact files changed, behavior established, tests, API impact, provider impact, and preserved non-goals.
7. Stop for review.

Do not implement the entire feature.
```
