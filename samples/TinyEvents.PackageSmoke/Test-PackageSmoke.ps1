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
$nugetConfig = Join-Path $artifactRoot "NuGet.config"

New-Item -ItemType Directory -Force -Path $packagesDirectory | Out-Null

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
