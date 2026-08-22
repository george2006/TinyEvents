# Roadmap

TinyEvents is moving from feature completion into contract stabilization and production-readiness validation.

## 0.1.0-alpha.3

The final alpha establishes the complete initial product shape:

- provider-agnostic transactional outbox publishing and processing
- generated consumer registration and event dispatch without runtime scanning
- SQL Server and PostgreSQL providers for ADO.NET and EF Core
- lease-based multi-worker claiming and at-least-once delivery
- resilient worker failure handling, startup validation, and structured logging
- explicit forward-only built-in migrations with history, locking, and existing-alpha baselining
- one coordinated six-package release train
- real-database, package-consumer, and published-alpha upgrade tests

## 0.1.0-beta.1

The beta milestone stabilizes the contracts established by the final alpha:

- review and freeze the intended public API surface
- freeze migration identifiers, history shape, checksum behavior, and logging event IDs
- stabilize worker failure, retry, recovery, and cancellation behavior
- establish automated public API and package compatibility checks
- verify package metadata, dependency alignment, and clean-consumer installation
- tighten diagnostics, examples, deployment guidance, and upgrade documentation
- measure representative processing, claiming, and migration behavior under load
- validate bounded processed-message cleanup under concurrent publication and processing

## 1.0.0

The 1.0 release requires evidence that the stabilized contracts are ready for production use:

- SQL Server and PostgreSQL runtime suites pass with no skipped database tests
- concurrent claiming and migration behavior remains deterministic
- supported upgrade paths are repeatable from published packages
- package contents, dependencies, and public APIs pass compatibility gates
- failure, cancellation, retry, lease-loss, and recovery behavior is documented and tested
- security, SQL-safety, logging-safety, and operational reviews have no release blockers
- installation, migration, deployment, recovery, and troubleshooting guidance is complete
- no known correctness issue remains in the supported provider matrix
- processed outbox growth is bounded by an evidence-backed retention policy
