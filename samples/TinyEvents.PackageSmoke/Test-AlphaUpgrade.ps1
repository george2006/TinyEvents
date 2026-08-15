param(
    [string]$AlphaVersion = "0.1.0-alpha.2",
    [string]$PackageVersion = ""
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
$project = Join-Path $PSScriptRoot "..\TinyEvents.AlphaUpgradeSmoke\TinyEvents.AlphaUpgradeSmoke.csproj"
$composeFile = Join-Path $PSScriptRoot "docker-compose.yml"

if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $PackageVersion = "0.1.0-local.upgrade.$(Get-Date -Format 'yyyyMMddHHmmss')"
}

& (Join-Path $PSScriptRoot "Test-PackageSmoke.ps1") -PackageVersion $PackageVersion

$artifactRoot = Join-Path $repoRoot "artifacts\package-smoke\$PackageVersion"
$packagesDirectory = Join-Path $artifactRoot "packages"
$upgradeRoot = Join-Path $artifactRoot "alpha-upgrade"
$alphaCache = Join-Path $upgradeRoot "alpha-cache"
$localCache = Join-Path $upgradeRoot "local-cache"
$alphaObj = Join-Path $upgradeRoot "alpha-obj\"
$localObj = Join-Path $upgradeRoot "local-obj\"
$alphaBin = Join-Path $upgradeRoot "alpha-bin\"
$localBin = Join-Path $upgradeRoot "local-bin\"
$alphaConfig = Join-Path $upgradeRoot "NuGet.alpha.config"
$localConfig = Join-Path $upgradeRoot "NuGet.local.config"

New-Item -ItemType Directory -Force -Path $alphaCache, $localCache | Out-Null

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -Path $alphaConfig -Encoding UTF8

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="TinyEventsLocal" value="$packagesDirectory" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -Path $localConfig -Encoding UTF8

Invoke-Native "docker" @("compose", "-f", $composeFile, "up", "-d", "--wait")

$databaseSuffix = Get-Date -Format "yyyyMMddHHmmssfff"
$env:TINYEVENTS_ALPHA_UPGRADE_SQLSERVER = "Server=localhost,14334;Database=TinyEventsAlphaUpgrade$databaseSuffix;User Id=sa;Password=TinyEvents_2026!;Encrypt=False;TrustServerCertificate=True;"
$env:TINYEVENTS_ALPHA_UPGRADE_POSTGRESQL = "Host=localhost;Port=54324;Database=tinyevents_alpha_upgrade_$databaseSuffix;Username=postgres;Password=postgres;"

Write-Host "Creating legacy schemas with published TinyEvents $AlphaVersion packages..."
$env:NUGET_PACKAGES = $alphaCache
Invoke-Native "dotnet" @(
    "restore", $project,
    "--configfile", $alphaConfig,
    "--no-cache", "--force",
    "/p:TinyEventsPackageVersion=$AlphaVersion",
    "/p:LegacySchema=true",
    "/p:BaseIntermediateOutputPath=$alphaObj")
Invoke-Native "dotnet" @(
    "build", $project, "-c", "Release", "--no-restore",
    "/p:TinyEventsPackageVersion=$AlphaVersion",
    "/p:LegacySchema=true",
    "/p:BaseIntermediateOutputPath=$alphaObj",
    "/p:OutputPath=$alphaBin")
Invoke-Native "dotnet" @((Join-Path $alphaBin "TinyEvents.AlphaUpgradeSmoke.dll"))

Write-Host "Upgrading the consumer to locally packed TinyEvents $PackageVersion packages..."
$env:NUGET_PACKAGES = $localCache
Invoke-Native "dotnet" @(
    "restore", $project,
    "--configfile", $localConfig,
    "--no-cache", "--force",
    "/p:TinyEventsPackageVersion=$PackageVersion",
    "/p:BaseIntermediateOutputPath=$localObj")
Invoke-Native "dotnet" @(
    "build", $project, "-c", "Release", "--no-restore",
    "/p:TinyEventsPackageVersion=$PackageVersion",
    "/p:BaseIntermediateOutputPath=$localObj",
    "/p:OutputPath=$localBin")
Invoke-Native "dotnet" @((Join-Path $localBin "TinyEvents.AlphaUpgradeSmoke.dll"))

Write-Host "Published-alpha to built-in-migrations upgrade smoke passed."
