# TinyEvents Samples

The app samples are split by database family and provider style.

Each sample bootstraps a disposable business schema for local use, applies the
TinyEvents migrations explicitly, and then starts the processing and cleanup
hosted services. Production applications should manage their business schema
through their normal deployment process and retain the same
`Build -> MigrateTinyEventsAsync -> RunAsync` ordering.

## Prerequisites

- .NET 8 SDK
- Docker Desktop or another Docker engine

## 1. Start Databases

```bash
docker compose up -d sqlserver postgresql
```

Default SQL Server connection string:

```text
Server=localhost,14333;Database=TinyEventsSamples;User Id=sa;Password=TinyEvents_2026!;Encrypt=False;TrustServerCertificate=True
```

Default PostgreSQL connection string:

```text
Host=localhost;Port=54323;Database=tinyevents_samples;Username=postgres;Password=postgres;
```

You can pass the connection string as the first command-line argument, or set:

```powershell
$env:TINYEVENTS_SAMPLE_SQLSERVER = "Server=localhost,14333;Database=TinyEventsSamples;User Id=sa;Password=TinyEvents_2026!;Encrypt=False;TrustServerCertificate=True"
$env:TINYEVENTS_SAMPLE_POSTGRESQL = "Host=localhost;Port=54323;Database=tinyevents_samples;Username=postgres;Password=postgres;"
```

## 2. Run An App Sample

### SQL Server EF Core

Runs from local project references and demonstrates SQL Server EF Core publishing:

```bash
dotnet run --project samples/TinyEvents.Sample.EfCore
```

The EF Core sample uses `Database.EnsureCreatedAsync()` to bootstrap its
disposable demo schema, including the initial outbox. It then calls
`MigrateTinyEventsAsync`, which records the existing outbox baseline and applies
pending migrations, before `RunAsync`. The registered TinyEvents worker
processes messages automatically and runs bounded processed-message cleanup.

### SQL Server ADO.NET

Runs from local project references and demonstrates SQL Server application-owned ADO.NET transactions:

```bash
dotnet run --project samples/TinyEvents.Sample.AdoNet
```

The ADO.NET sample creates its disposable demo tables, calls
`MigrateTinyEventsAsync`, and only then runs the host. The registered TinyEvents
worker processes messages automatically and runs bounded processed-message
cleanup.

### PostgreSQL EF Core

Runs from local project references and demonstrates PostgreSQL EF Core publishing:

```bash
dotnet run --project samples/TinyEvents.Sample.PostgreSql.EfCore
```

The PostgreSQL EF Core sample uses `Database.EnsureCreatedAsync()` to bootstrap
its disposable demo schema, including the initial outbox. It then records that
baseline and applies pending TinyEvents migrations before running the host. The
registered worker processes messages and cleanup automatically.

### PostgreSQL ADO.NET

Runs from local project references and demonstrates PostgreSQL application-owned ADO.NET transactions:

```bash
dotnet run --project samples/TinyEvents.Sample.PostgreSql.AdoNet
```

The PostgreSQL ADO.NET sample creates its disposable demo tables, migrates
TinyEvents, and only then runs the host. The registered worker processes
messages and cleanup automatically.

## 3. Try The Endpoints

Create a user:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5000/users `
  -ContentType "application/json" `
  -Body '{"email":"ada@example.com"}'
```

The hosted worker claims and processes the outbox message automatically. Read
the consumer log after the next polling iteration:

```powershell
Invoke-RestMethod -Method Get -Uri http://localhost:5000/welcome-emails
```

## Package Smoke Sample

`TinyEvents.PackageSmoke` references NuGet packages instead of local project
references and is intentionally excluded from `TinyEvents.sln`. Validate locally
packed packages with:

```powershell
.\samples\TinyEvents.PackageSmoke\Test-PackageSmoke.ps1
```

Use it against published packages with:

```bash
docker compose -f samples/TinyEvents.PackageSmoke/docker-compose.yml up -d
dotnet run --project samples/TinyEvents.PackageSmoke/TinyEvents.PackageSmoke.csproj
```

It uses a separate SQL Server port, `14334`, so it can run beside the main development SQL Server container.
