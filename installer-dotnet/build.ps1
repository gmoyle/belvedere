#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Belvedere .NET installer (MSI) end to end.

.DESCRIPTION
    1. Publishes Belvedere as a self-contained, single-file win-x64 exe.
    2. Packages it into a per-machine MSI using the WiX v5 toolset.

    Prerequisites:
      * .NET SDK 8+            (dotnet)
      * WiX v5 tool            (dotnet tool install --global wix)

    Run from anywhere:
      pwsh installer-dotnet/build.ps1
      pwsh installer-dotnet/build.ps1 -Version 2.1.0
#>
param(
    [string]$Version = "2.0.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

# Repo root is the parent of this script's folder.
$root      = Split-Path -Parent $PSScriptRoot
$project   = Join-Path $root "src/Belvedere/Belvedere.csproj"
$publishDir= Join-Path $root "dist/$Runtime"
$iconFile  = Join-Path $root "src/Belvedere/resources/belvedere.ico"
$wxs       = Join-Path $PSScriptRoot "Belvedere.wxs"
$msiOut    = Join-Path $root "dist/Belvedere-$Version.msi"

Write-Host "==> Publishing self-contained single-file exe ($Runtime)..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $project -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Host "==> Building MSI with WiX..." -ForegroundColor Cyan
wix build $wxs -o $msiOut -d PublishDir=$publishDir -d IconFile=$iconFile -d ProductVersion=$Version
if ($LASTEXITCODE -ne 0) { throw "wix build failed." }

Write-Host ""
Write-Host "Done: $msiOut" -ForegroundColor Green
Get-Item $msiOut | Select-Object Name, @{n="SizeMB";e={[math]::Round($_.Length/1MB,1)}}
