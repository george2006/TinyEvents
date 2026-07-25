# Releasing

TinyEvents uses one release train.

All published TinyEvents packages move together with the same version. The shared development version lives in `Directory.Build.props`. Release workflows override that version from the git tag when packages are packed.

## Full Release Train

The `Release` workflow runs when a tag matching `v*` is pushed.

Example:

```text
v0.1.0-alpha.2
```

This workflow:

- restores the solution
- builds the solution in Release
- runs the full test suite with SQL Server and PostgreSQL integration tests enabled
- packs every NuGet package with the tag version
- verifies every expected package exists
- publishes every package to NuGet

The release train publishes:

- `TinyEvents`
- `TinyEvents.Worker`
- `TinyEvents.SqlServer.AdoNet`
- `TinyEvents.SqlServer.EntityFrameworkCore`
- `TinyEvents.PostgreSql.AdoNet`
- `TinyEvents.PostgreSql.EntityFrameworkCore`

Do not publish a provider package independently during the alpha hardening phase. Provider packages reference `TinyEvents`, and keeping one version across the train avoids accidental dependency mismatches.

## Local Checks

Before using either release path, run:

```powershell
dotnet restore
dotnet build TinyEvents.sln -c Release --no-restore
dotnet test TinyEvents.sln -c Release --no-build --no-restore
```

For database runtime confidence, enable the integration test lanes locally:

```powershell
$env:TINYEVENTS_RUN_SQLSERVER_TESTS = "true"
$env:TINYEVENTS_RUN_POSTGRESQL_TESTS = "true"
dotnet test TinyEvents.sln -c Release --no-build --no-restore
```

The GitHub workflows run the integration lanes before publishing.
