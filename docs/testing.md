# Testing

TinyEvents has two test lanes.

## Normal Suite

Run the normal behavior suite:

```bash
dotnet test TinyEvents.sln --no-restore
```

This lane uses fakes and in-memory test doubles. It should stay fast and should not require Docker.

It covers:

- publisher behavior
- processor behavior
- in-memory claim lifecycle
- source generator output, contribution bootstrap, and runtime registration
- EF Core provider mapping and SQL shape
- ADO.NET transaction ownership and SQL shape
- worker service registration and scoped processing

## SQL Server Runtime Suite

The SQL Server runtime tests live in:

```text
tests/TinyEvents.SqlServer.Tests
```

They use Testcontainers and are skipped unless this environment variable is set:

```powershell
$env:TINYEVENTS_RUN_SQLSERVER_TESTS = "true"
dotnet test tests\TinyEvents.SqlServer.Tests\TinyEvents.SqlServer.Tests.csproj
```

These tests start an ephemeral SQL Server container and prove behavior fakes cannot prove:

- ADO.NET business data and outbox messages commit together.
- ADO.NET business data and outbox messages roll back together.
- Competing workers do not claim the same message.
- Expired processing leases can be reclaimed.
- Active processing leases are not reclaimed.
- EF Core SQL Server claim SQL works against the real database.

## Local SQL Server

For manual development:

```bash
docker compose up -d sqlserver
```

Connection details:

```text
Server=localhost,14333
User Id=sa
Password=TinyEvents_2026!
TrustServerCertificate=True
```

## Running Samples

Run the ADO.NET sample:

```bash
dotnet run --project samples/TinyEvents.Sample.AdoNet -- "Server=localhost,14333;User Id=sa;Password=TinyEvents_2026!;TrustServerCertificate=True"
```

Create a user:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5000/users `
  -ContentType "application/json" `
  -Body '{"email":"ada@example.com"}'
```

Process one outbox batch:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/outbox/process
```

Run the EF Core sample:

```bash
dotnet run --project samples/TinyEvents.Sample.EfCore -- "Server=localhost,14333;User Id=sa;Password=TinyEvents_2026!;TrustServerCertificate=True"
```

It exposes the same sample endpoints.
