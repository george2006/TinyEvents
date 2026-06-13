# TinyEvents Samples

The app samples are split by database family and provider style.

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

The EF Core sample creates its database schema with `Database.EnsureCreatedAsync()` for local demo purposes.

### SQL Server ADO.NET

Runs from local project references and demonstrates SQL Server application-owned ADO.NET transactions:

```bash
dotnet run --project samples/TinyEvents.Sample.AdoNet
```

The ADO.NET sample creates the demo `Users` table and `TinyOutbox` table on startup for local demo purposes. Real applications should run the TinyEvents outbox SQL through their normal migration tool.

### PostgreSQL EF Core

Runs from local project references and demonstrates PostgreSQL EF Core publishing:

```bash
dotnet run --project samples/TinyEvents.Sample.PostgreSql.EfCore
```

The PostgreSQL EF Core sample creates its database schema with `Database.EnsureCreatedAsync()` for local demo purposes.

### PostgreSQL ADO.NET

Runs from local project references and demonstrates PostgreSQL application-owned ADO.NET transactions:

```bash
dotnet run --project samples/TinyEvents.Sample.PostgreSql.AdoNet
```

The PostgreSQL ADO.NET sample creates the demo `Users` table and `TinyOutbox` table on startup for local demo purposes. Real applications should run the TinyEvents outbox SQL through their normal migration tool.

## 3. Try The Endpoints

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

Read the consumer log:

```powershell
Invoke-RestMethod -Method Get -Uri http://localhost:5000/welcome-emails
```

## Package Smoke Sample

`TinyEvents.PackageSmoke` references the public NuGet packages instead of local project references. Use it after publishing packages:

```bash
docker compose -f samples/TinyEvents.PackageSmoke/docker-compose.yml up -d
dotnet run --project samples/TinyEvents.PackageSmoke/TinyEvents.PackageSmoke.csproj
```

It uses a separate SQL Server port, `14334`, so it can run beside the main development SQL Server container.
