# Changelog

## Unreleased

### Added

- `ITinyOutboxStatusReader` and `TinyOutboxStatus` for observing one configured
  outbox without loading event payloads.
- Scoped reader registration in all four built-in SQL Server and PostgreSQL
  providers, through their existing setup methods.
- Counts of pending, processing, expired processing leases and terminally failed
  messages, plus oldest outstanding age and latest retained processing timestamp.
- [Operational status documentation](docs/operations.md) and
  [upgrade guidance](docs/upgrading-to-beta-2.md).

### Compatibility

- The capability is additive. Existing publishing, processing and cleanup
  interfaces are unchanged; custom providers do not have to implement the reader.
- No new migration or index. Databases remain on schema version 2.
- Observations respect database isolation and are not worker heartbeats or
  synchronization barriers. Cleanup can remove the last retained success timestamp.
- Exact counts have a database cost. The
  [PostgreSQL measurements](docs/performance/postgresql-outbox-status.md) describe
  local synthetic results, not production performance guarantees.

## Earlier Releases

Release notes preceding this changelog are available in
[GitHub Releases](https://github.com/george2006/TinyEvents/releases).
