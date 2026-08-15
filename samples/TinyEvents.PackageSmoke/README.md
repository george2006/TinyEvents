# TinyEvents Package Smoke

This project is intentionally not part of `TinyEvents.sln`. It consumes the
locally packed NuGet packages, so including it would make a normal solution
restore depend on packages that have not been built yet.

## Local Package Build Smoke

Run this sample against locally packed packages without touching a database:

```powershell
.\samples\TinyEvents.PackageSmoke\Test-PackageSmoke.ps1
```

That command builds the solution, packs the complete TinyEvents release train with a unique local version, verifies that migration code remains inside the existing provider assemblies, and restores/builds this sample against an empty isolated NuGet cache.

## Local Package Runtime Smoke

To start the sample SQL Server and PostgreSQL containers and run the database smoke paths:

```powershell
.\samples\TinyEvents.PackageSmoke\Test-PackageSmoke.ps1 -StartDatabases -Run
```

To run against already-started databases, omit `-StartDatabases`:

```powershell
.\samples\TinyEvents.PackageSmoke\Test-PackageSmoke.ps1 -Run
```

`-Run` uses the default SQL Server port `14334` and PostgreSQL port `54324` unless the environment variables below override them.

To run this sample after publishing packages to NuGet:

```bash
docker compose -f samples/TinyEvents.PackageSmoke/docker-compose.yml up -d
dotnet run --project samples/TinyEvents.PackageSmoke/TinyEvents.PackageSmoke.csproj
```

The default connection string targets the SQL Server container on port `14334`.
The PostgreSQL smoke path targets the PostgreSQL container on port `54324`.

To use a different SQL Server instance:

```bash
set TINYEVENTS_PACKAGE_SMOKE_SQLSERVER=Server=localhost,1433;Database=TinyEventsPackageSmoke;User Id=sa;Password=your-password;Encrypt=False;TrustServerCertificate=True;
```

To use a different PostgreSQL instance:

```bash
set TINYEVENTS_PACKAGE_SMOKE_POSTGRESQL=Host=localhost;Port=5432;Database=TinyEventsPackageSmoke;Username=postgres;Password=your-password;
```

The sample references package versions through `TinyEventsPackageVersion`, which defaults to the shared version from `Directory.Build.props`. The local smoke script overrides that property with the temporary locally packed package version.

It verifies that the core package, SQL Server providers, PostgreSQL providers, worker package, public migration entry point, dependency injection extensions, source-generator consumer registration, publishing, claiming, and processing can be consumed from NuGet packages instead of project references. Runtime smoke calls `MigrateTinyEventsAsync` through all four packaged providers.
