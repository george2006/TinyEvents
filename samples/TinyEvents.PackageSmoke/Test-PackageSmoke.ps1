param(
    [string]$PackageVersion = "",
    [switch]$Run,
    [switch]$StartDatabases
)

$ErrorActionPreference = "Stop"

function Invoke-Native {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$solution = Join-Path $repoRoot "TinyEvents.sln"
$sampleProject = Join-Path $repoRoot "samples\TinyEvents.PackageSmoke\TinyEvents.PackageSmoke.csproj"
$composeFile = Join-Path $repoRoot "samples\TinyEvents.PackageSmoke\docker-compose.yml"

if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $PackageVersion = "0.1.0-local.$(Get-Date -Format 'yyyyMMddHHmmss')"
}

$artifactRoot = Join-Path $repoRoot "artifacts\package-smoke\$PackageVersion"
$packagesDirectory = Join-Path $artifactRoot "packages"
$packageCacheDirectory = Join-Path $artifactRoot "nuget-cache"
$nugetConfig = Join-Path $artifactRoot "NuGet.config"

New-Item -ItemType Directory -Force -Path $packagesDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $packageCacheDirectory | Out-Null

$projects = @(
    "src\TinyEvents\TinyEvents.csproj",
    "src\TinyEvents.Worker\TinyEvents.Worker.csproj",
    "src\TinyEvents.SqlServer.AdoNet\TinyEvents.SqlServer.AdoNet.csproj",
    "src\TinyEvents.SqlServer.EntityFrameworkCore\TinyEvents.SqlServer.EntityFrameworkCore.csproj",
    "src\TinyEvents.PostgreSql.AdoNet\TinyEvents.PostgreSql.AdoNet.csproj",
    "src\TinyEvents.PostgreSql.EntityFrameworkCore\TinyEvents.PostgreSql.EntityFrameworkCore.csproj"
)

Write-Host "Building TinyEvents release train..."
Invoke-Native "dotnet" @("restore", $solution)
Invoke-Native "dotnet" @("build", $solution, "-c", "Release", "--no-restore")

Write-Host "Packing TinyEvents release train as $PackageVersion..."
foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    Invoke-Native "dotnet" @(
        "pack",
        $projectPath,
        "-c",
        "Release",
        "--no-build",
        "-o",
        $packagesDirectory,
        "/p:PackageVersion=$PackageVersion",
        "/p:Version=$PackageVersion")
}

$expectedPackageIds = @(
    "TinyEvents",
    "TinyEvents.Worker",
    "TinyEvents.SqlServer.AdoNet",
    "TinyEvents.SqlServer.EntityFrameworkCore",
    "TinyEvents.PostgreSql.AdoNet",
    "TinyEvents.PostgreSql.EntityFrameworkCore"
)

$packageFiles = @(Get-ChildItem -LiteralPath $packagesDirectory -Filter "*.nupkg" -File |
    Where-Object { -not $_.Name.EndsWith(".snupkg", [StringComparison]::OrdinalIgnoreCase) })

if ($packageFiles.Count -ne $expectedPackageIds.Count) {
    throw "Expected $($expectedPackageIds.Count) packages but found $($packageFiles.Count)."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($packageId in $expectedPackageIds) {
    $expectedFileName = "$packageId.$PackageVersion.nupkg"
    $packageFile = $packageFiles | Where-Object {
        $_.Name.Equals($expectedFileName, [StringComparison]::OrdinalIgnoreCase)
    }

    if ($null -eq $packageFile) {
        throw "Expected package was not produced: $expectedFileName"
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($packageFile.FullName)

    try {
        $unexpectedMigrationAssembly = $archive.Entries | Where-Object {
            $_.FullName -match '(^|/)TinyEvents\..*Migrations\.dll$'
        }

        if ($null -ne $unexpectedMigrationAssembly) {
            throw "Package $packageId contains an unexpected migration assembly: $($unexpectedMigrationAssembly.FullName)"
        }

        if ($packageId -like "TinyEvents.*.*") {
            $providerAssembly = "lib/net8.0/$packageId.dll"
            $containsProviderAssembly = $archive.Entries | Where-Object {
                $_.FullName.Equals($providerAssembly, [StringComparison]::OrdinalIgnoreCase)
            }

            if ($null -eq $containsProviderAssembly) {
                throw "Package $packageId does not contain its compiled provider assembly."
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

Write-Host "Verified the existing package set and in-package provider assemblies."

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="TinyEventsLocal" value="$packagesDirectory" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -Path $nugetConfig -Encoding UTF8

Write-Host "Restoring package smoke sample from local TinyEvents packages..."
$env:NUGET_PACKAGES = $packageCacheDirectory
Invoke-Native "dotnet" @(
    "restore",
    $sampleProject,
    "--configfile",
    $nugetConfig,
    "--no-cache",
    "--force",
    "/p:TinyEventsPackageVersion=$PackageVersion")

Write-Host "Building package smoke sample..."
Invoke-Native "dotnet" @(
    "build",
    $sampleProject,
    "-c",
    "Release",
    "--no-restore",
    "/p:TinyEventsPackageVersion=$PackageVersion")

if ($StartDatabases) {
    Write-Host "Starting package smoke databases..."
    Invoke-Native "docker" @("compose", "-f", $composeFile, "up", "-d", "--wait")
}

if ($Run) {
    Write-Host "Running package smoke sample..."
    Invoke-Native "dotnet" @(
        "run",
        "--project",
        $sampleProject,
        "-c",
        "Release",
        "--no-build",
        "--no-restore")
}

Write-Host "TinyEvents package smoke validation passed for $PackageVersion."
