# Upgrading To 1.0.0-beta.2

**Unreleased.** Installation commands apply once `1.0.0-beta.2` is published.

## What Changes

The release adds `ITinyOutboxStatusReader` and `TinyOutboxStatus` in the `TinyEvents`
namespace. All four built-in database providers register the reader as scoped
through their existing setup methods. Hosts can observe backlog, expired leases,
terminal failures and retained successful processing without reading payloads.

See [Outbox Operational Status](operations.md) for usage and exact semantics.
There is no HTTP service, worker heartbeat, automatic monitoring loop or replay
API. Publishing, processing, retry and cleanup behavior remain unchanged by this
feature. It remains a pre-1.0 beta with at-least-once delivery.

## Upgrade The Train Together

Keep all TinyEvents packages used by an application on the same version. For a
SQL Server EF Core application, after publication:

```bash
dotnet add package TinyEvents --version 1.0.0-beta.2
dotnet add package TinyEvents.SqlServer.EntityFrameworkCore --version 1.0.0-beta.2
dotnet add package TinyEvents.Worker --version 1.0.0-beta.2
```

Use the corresponding PostgreSQL or ADO.NET package where appropriate. There is
no separate operations package. The generator remains bundled in the core package.
Existing custom providers need no new members on their store or cleanup interfaces;
they only need to implement/register the reader if an application uses it.

## Schema And Deployment

This feature adds no migration, table or index. A database already migrated by
`beta.1` remains at schema version 2, and migration execution should be a no-op.
Keep the existing explicit startup migration step for fresh databases and upgrades
from older versions. Applications upgrading from an alpha must also follow the
[beta.1 upgrade guide](upgrading-to-beta-1.md).

The observation query respects existing isolation. It is not proof of worker
liveness or a barrier for draining an outbox. Cleanup can remove the latest
success timestamp, and pending counts include future retries.

## Verify In Your Application

1. Confirm all TinyEvents references resolve to the same beta version.
2. Apply the usual migration step; an existing beta.1 database should stay at version 2.
3. Confirm normal publication, processing and cleanup still work.
4. Resolve the reader in a scope configured for the intended outbox and call `ReadAsync`.
5. Handle read failures separately from an empty outbox and choose a bounded polling cadence.

Large-backlog queries have a measurable database cost. Local PostgreSQL evidence
is available in the [measurement report](performance/postgresql-outbox-status.md);
it does not establish cloud performance guarantees.
