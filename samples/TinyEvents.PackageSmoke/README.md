# TinyEvents Package Smoke

Run this sample after publishing packages to NuGet:

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

The sample references the public `0.1.0-alpha.2` packages instead of local project references. It verifies that the core package, SQL Server providers, PostgreSQL providers, worker package, dependency injection extensions, source-generator consumer registration, publishing, claiming, and processing can be consumed from NuGet.
