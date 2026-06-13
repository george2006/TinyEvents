# TinyEvents.PostgreSql.EntityFrameworkCore

PostgreSQL Entity Framework Core provider package for TinyEvents outbox storage and worker claiming.

This package boundary exists so PostgreSQL support can be built without changing TinyEvents core or the SQL Server providers.

Runtime behavior is being added in small reviewed slices. The provider will follow the same ownership model as the SQL Server EF Core provider:

- applications own the `DbContext`
- publishing adds an outbox message to the caller-owned `DbContext`
- TinyEvents does not call `SaveChangesAsync`
- business data and outbox messages commit together when the caller saves
- worker claiming uses PostgreSQL-specific SQL

PostgreSQL worker claiming will use `FOR UPDATE SKIP LOCKED`.

## More Documentation

- Project documentation: https://github.com/george2006/TinyEvents
- Worker guide: https://github.com/george2006/TinyEvents/blob/main/docs/workers.md
- Schema and migrations: https://github.com/george2006/TinyEvents/blob/main/docs/schema-and-migrations.md
