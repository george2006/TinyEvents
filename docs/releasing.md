# Releasing

TinyEvents has two release paths.

Use the full release workflow when every package should move together. Use the package release workflow when one package needs its own version bump.

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

Use this when the suite should share one version:

- `TinyEvents`
- `TinyEvents.Worker`
- `TinyEvents.SqlServer.AdoNet`
- `TinyEvents.SqlServer.EntityFrameworkCore`
- `TinyEvents.PostgreSql.AdoNet`
- `TinyEvents.PostgreSql.EntityFrameworkCore`

## Single Package Release

The `Release Package` workflow is manual.

Use it when one package needs a dedicated version without forcing every package to publish the same version.

Inputs:

- `package`: the package id to publish
- `version`: the exact NuGet version to publish

Example:

```text
package: TinyEvents.PostgreSql.AdoNet
version: 0.1.0-alpha.3
```

This workflow still restores, builds, and tests the whole solution before publishing. It only packs and pushes the selected package.

Use this for:

- provider-specific alpha fixes
- package metadata fixes
- documentation or schema-content fixes that affect one package
- small package-specific corrections that do not require a release train

## Before A Single Package Release

Check the selected package dependency story before publishing.

Provider packages reference `TinyEvents` in the repository through project references. During packing, NuGet dependencies are generated from those project references. Make sure the dependency version is the one you intend consumers to use.

If the provider package requires a newer core package, publish the core package first or use the full release train.

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
