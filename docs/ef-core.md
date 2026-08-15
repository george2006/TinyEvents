# EF Core Providers

TinyEvents EF Core providers store outbox messages through a caller-owned `DbContext`.

Publishing adds an outbox entity to the scoped `DbContext`. TinyEvents does not call `SaveChangesAsync`; the caller owns the save boundary.

Choose the provider for your database:

- [SQL Server EF Core](sql-server/ef-core.md)
- [PostgreSQL EF Core](postgresql/ef-core.md)

Register exactly one TinyEvents database provider per service collection.

Both providers use the same core publishing model:

```csharp
dbContext.Users.Add(user);
await events.PublishAsync(new UserCreated(user.Id, user.Email), ct);
await dbContext.SaveChangesAsync(ct);
```

Worker claiming is database-specific:

- SQL Server uses SQL Server locking hints and atomic update/output SQL.
- PostgreSQL uses `FOR UPDATE SKIP LOCKED` inside an atomic update/returning SQL statement.

Worker claim and mark operations use the relational connection from the scoped `DbContext`.

If `DbContext.Database.CurrentTransaction` exists, TinyEvents attaches the claim or mark command to that transaction. TinyEvents does not begin, commit, or roll back an EF Core transaction for worker operations.

The hosted worker creates a fresh dependency-injection scope for each processing iteration. In normal hosted-worker usage, claim and mark commands run without an application transaction unless your application explicitly creates one in that worker scope.
