#Requires -Version 5.0
<#
.SYNOPSIS
    Builds the mod in Release and packages GameData into a versioned zip.

.DESCRIPTION
    Builds the project (which makes KSPBT populate the
    plugin and the GameData .version file), reads the resulting version, and
    zips the GameData folder into Builds\<modName>-<version>.zip.

    No external tools required.
#>

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot

# Derive the mod name from the .version file at the repo root.
$versionFiles = @(Get-ChildItem -Path $root -Filter *.version -File)
if ($versionFiles.Count -ne 1) {
    throw "Expected exactly one .version file in $root, found $($versionFiles.Count)."
}
$modName = $versionFiles[0].BaseName

# Build in Release. SolutionDir is passed explicitly so RepoRoot resolves
# the same way it does when building through the solution in the IDE.
Write-Host "Building $modName (Release)..." -ForegroundColor Cyan
dotnet build "$root\$modName\$modName.csproj" -c Release -p:SolutionDir="$root\"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# Read the version KSPBT wrote into the packaged .version file.
$versionFile = Join-Path $root "GameData\$modName\$modName.version"
$version = (Get-Content $versionFile -Raw | ConvertFrom-Json).VERSION
Write-Host "Version: $version" -ForegroundColor Cyan

# Pull Agencies from the single source of truth (KP-Core), pinned.
$kpCoreRef = "main"   # or a tag/commit for reproducible releases
$agenciesSrc = "GameData/KerbalPowers/Agencies"
$dest = Join-Path $root "GameData\KerbalPowers"

$tmp = Join-Path $env:TEMP "kp-core-$([guid]::NewGuid())"
git clone --depth 1 --filter=blob:none --sparse `
    --branch $kpCoreRef https://github.com/KerbalPowers/KP-Core.git $tmp
git -C $tmp sparse-checkout set $agenciesSrc
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $tmp $agenciesSrc) $dest -Recurse -Force
Remove-Item $tmp -Recurse -Force

# Delete thumbnail caches.
Get-ChildItem (Join-Path $root "GameData") -Directory -Recurse -Filter "@thumbs" | Remove-Item -Recurse -Force

# Package GameData into Builds\<mod>-<version>.zip.
$buildsDir = Join-Path $root "Builds"
New-Item -ItemType Directory -Force -Path $buildsDir | Out-Null

$zip = Join-Path $buildsDir "$modName-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

Compress-Archive -Path (Join-Path $root "GameData") -DestinationPath $zip
Write-Host "Created $zip" -ForegroundColor Green
