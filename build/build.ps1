# Build script for Torrent7.
# Runs the tests, publishes single-file self-contained builds for
# win-x64, linux-x64 and linux-arm64, and packs them into dist\.
# Usage: powershell -ExecutionPolicy Bypass -File build\build.ps1 [-Version 1.2.3] [-SkipTests]

param(
    [string]$Version,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$csprojPath = Join-Path $root 'src\Torrent7\Torrent7.csproj'

if (-not $Version) {
    if (-not (Test-Path $csprojPath)) { throw "Missing $csprojPath" }
    [xml]$proj = Get-Content -Raw $csprojPath
    $Version = $proj.Project.PropertyGroup |
        ForEach-Object { $_.Version } |
        Where-Object { $_ } |
        Select-Object -First 1
    if (-not $Version) { throw 'No <Version> found in Torrent7.csproj.' }
}

Write-Host "Torrent7 build $Version"

# Step 1: tests
if (-not $SkipTests) {
    Write-Host 'Running tests...'
    dotnet run --project tests\Torrent7.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed, not publishing.' }
}

# Step 2: publish
$rids = @('win-x64', 'linux-x64', 'linux-arm64')
$publishArgs = @(
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=none',
    "-p:Version=$Version"
)

foreach ($rid in $rids) {
    Write-Host "Publishing $rid..."
    $outDir = Join-Path $root "publish\$rid"
    dotnet publish src\Torrent7\Torrent7.csproj -c Release -r $rid --self-contained true $publishArgs -o $outDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid." }
}

# Step 3: package
$distDir = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$artifacts = @()

$winZip = Join-Path $distDir "Torrent7-$Version-win-x64.zip"
if (Test-Path $winZip) { Remove-Item -Force $winZip }
Compress-Archive -Path (Join-Path $root 'publish\win-x64\*') -DestinationPath $winZip -Force
$artifacts += $winZip

foreach ($rid in @('linux-x64', 'linux-arm64')) {
    $tarGz = Join-Path $distDir "Torrent7-$Version-$rid.tar.gz"
    if (Test-Path $tarGz) { Remove-Item -Force $tarGz }
    tar -czf $tarGz -C (Join-Path $root "publish\$rid") .
    if ($LASTEXITCODE -ne 0) { throw "tar failed for $rid." }
    $artifacts += $tarGz
}

Write-Host ''
Write-Host 'Artifacts:'
foreach ($file in $artifacts) {
    $mb = (Get-Item $file).Length / 1MB
    $sizeText = $mb.ToString('F2', [System.Globalization.CultureInfo]::InvariantCulture)
    Write-Host ("  {0}  ({1} MB)" -f $file, $sizeText)
}
