# TinyEvents 0.1.0-alpha.3 Release Plan

Status: Planning
Target tag: `v0.1.0-alpha.3`
Release role: Final alpha before `0.1.0-beta.1`
Branch: `release/0.1.0-alpha.3`

## 1. Release Intent

`0.1.0-alpha.3` publishes the completed built-in migrations feature and closes
the alpha line with a package train that existing `alpha.2` applications can
upgrade safely.

The release is not a place for new architecture. Only release metadata,
documentation accuracy, acceptance fixes, and publishing automation fixes are
in scope. Any unrelated feature moves to the beta plan.

## 2. Release Train

Publish these six packages together at exactly `0.1.0-alpha.3`:

- `TinyEvents`
- `TinyEvents.Worker`
- `TinyEvents.SqlServer.AdoNet`
- `TinyEvents.SqlServer.EntityFrameworkCore`
- `TinyEvents.PostgreSql.AdoNet`
- `TinyEvents.PostgreSql.EntityFrameworkCore`

No migration package or migration DLL may be introduced.

## 3. Release Changes

### REL-1 — Version and package metadata

- Change `Directory.Build.props` from `0.1.0-alpha.2` to
  `0.1.0-alpha.3`.
- Update package descriptions that still name `alpha.2`.
- Update install commands in the root README, provider package READMEs, and
  user documentation.
- Keep historical references to `alpha.2` in the upgrade guide and upgrade
  smoke test.
- Verify every produced package reports version `0.1.0-alpha.3` and depends on
  the matching TinyEvents package version.

Stop for review and commit.

### REL-2 — Release notes and upgrade communication

Create release notes covering:

- explicit `MigrateTinyEventsAsync` lifecycle;
- SQL Server and PostgreSQL support across ADO.NET and EF Core;
- forward-only history, locking, concurrency, and idempotency;
- structured migration logs;
- existing `alpha.2` baseline behavior and its schema-shape limitation;
- the requirement to upgrade the complete package train;
- explicit non-goals: automatic startup migration, down migrations, and
  schema repair.

Link the release notes to `docs/upgrading-to-alpha-3.md`.

Stop for review and commit.

### REL-3 — Final acceptance gate

Run from a clean checkout:

1. `dotnet restore`;
2. Release solution build with zero warnings and errors;
3. all unit tests;
4. all SQL Server Testcontainers tests;
5. all PostgreSQL Testcontainers tests;
6. concurrent migration tests;
7. normal package smoke with an empty isolated NuGet cache;
8. published `alpha.2` to local `alpha.3` upgrade smoke;
9. public API audit;
10. package contents and dependency-version audit;
11. documentation link audit;
12. `git diff --check` and branch-scope review.

No skipped database tests are accepted in the final result.

Stop for review. Fix only demonstrated release blockers.

### REL-4 — Publish

- Merge the release branch to `main`.
- Confirm the merge commit is green in CI.
- Create annotated tag `v0.1.0-alpha.3` on the accepted `main` commit.
- Push the tag once.
- Monitor the Release workflow through build, tests, pack, NuGet publication,
  and GitHub release asset upload.
- Verify all six packages are visible on NuGet with the expected metadata and
  dependency versions.
- Install the published packages into the smoke consumer and run the database
  smoke one final time.

Do not recreate or move the tag after publication.

## 4. Beta Transition

After `alpha.3` publication is verified:

- start beta planning from the published `main` commit;
- use `0.1.0-beta.1` as the next public prerelease name;
- treat the migration history format, migration identifiers, checksums, public
  migration entry point, and logging event IDs as compatibility-sensitive;
- prioritize API stability, failure-policy completion, operational guidance,
  and release hardening over new provider breadth;
- require an explicit compatibility review for any breaking public or database
  contract change.

## 5. Definition Of Done

The alpha line is complete when:

- `v0.1.0-alpha.3` points at the accepted `main` commit;
- all six packages are published and independently installable;
- the real `alpha.2` upgrade succeeds on SQL Server and PostgreSQL;
- documentation consistently recommends `alpha.3` while preserving historical
  upgrade references;
- no release-blocking CI, packaging, or documentation issue remains;
- the next planned public version is `0.1.0-beta.1`.
