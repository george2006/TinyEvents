# Testing

TinyEvents separates fast behavior tests from opt-in real-database tests and package-consumer gates.

## Normal Suite

Run the solution suite without database integration tests:

```powershell
dotnet test TinyEvents.sln --no-restore
```

The real-database tests are discovered but skipped unless their environment switches are enabled. The normal lane covers:

- publisher and processor behavior
- retry, cancellation, lease-loss, and error persistence
- in-memory claim lifecycle
- generated dispatch, contribution bootstrap, and multi-assembly registration
- provider mapping, SQL shape, configuration, and connection ownership
- worker registration, startup validation, logging, and failure recovery
- migration models, catalogs, planning, checksums, SQL generation, and adapter ownership

## Complete Real-Database Suite

Enable both integration lanes and run the complete solution:

```powershell
$env:TINYEVENTS_RUN_SQLSERVER_TESTS = "true"
$env:TINYEVENTS_RUN_POSTGRESQL_TESTS = "true"
dotnet test TinyEvents.sln -c Release --no-build --no-restore
```

Release acceptance requires zero skipped database tests.

## SQL Server Runtime Projects

SQL Server behavior is split by provider style:

```text
tests/TinyEvents.SqlServer.AdoNet.Tests
tests/TinyEvents.SqlServer.EntityFrameworkCore.Tests
```

Run either project with `TINYEVENTS_RUN_SQLSERVER_TESTS=true`, or use the complete solution command above.

The real SQL Server lane proves:

- ADO.NET transaction commit and rollback ownership
- EF Core publishing and provider-specific claim/mark SQL
- active-lease exclusion and expired-lease reclamation
- competing-worker claim safety
- migration history transactions and exact timestamps
- migration locking, timeout, cancellation, and concurrent serialization
- fresh migration, current/no-op, alpha baseline, retry, and rollback behavior
- ADO.NET and EF Core migration connection ownership

## PostgreSQL Runtime Projects

PostgreSQL behavior is split by provider style:

```text
tests/TinyEvents.PostgreSql.AdoNet.Tests
tests/TinyEvents.PostgreSql.EntityFrameworkCore.Tests
```

Run either project with `TINYEVENTS_RUN_POSTGRESQL_TESTS=true`, or use the complete solution command above.

The real PostgreSQL lane proves:

- ADO.NET transaction commit and rollback ownership
- EF Core publishing and PostgreSQL claim/mark SQL
- active-lease exclusion and expired-lease reclamation
- competing-worker claim safety
- migration history transactions and exact timestamps
- advisory locking, timeout, cancellation, and concurrent serialization
- fresh migration, current/no-op, alpha baseline, retry, and rollback behavior
- ADO.NET and EF Core migration connection ownership

## Package Consumer Gates

Build all packages, inspect their contents, and restore/build an external consumer through an empty isolated NuGet cache:

```powershell
.\samples\TinyEvents.PackageSmoke\Test-PackageSmoke.ps1
```

Run the packaged providers against SQL Server and PostgreSQL:

```powershell
.\samples\TinyEvents.PackageSmoke\Test-PackageSmoke.ps1 -StartDatabases -Run
```

Prove the real upgrade journey from published `0.1.0-alpha.2` packages to locally packed `0.1.0-alpha.3` packages:

```powershell
.\samples\TinyEvents.PackageSmoke\Test-AlphaUpgrade.ps1
```

That gate creates the legacy schemas with the published alpha packages, upgrades the consumer package train, runs `MigrateTinyEventsAsync`, and verifies SQL Server and PostgreSQL baseline history.

## Local Sample Databases

For manual development and app samples, start both databases:

```powershell
docker compose up -d sqlserver postgresql
```

The complete sample runbook, ports, connection strings, and endpoints live in [TinyEvents Samples](../samples/README.md).
