<#
.SYNOPSIS
  Builds the TTSFreeUse Windows installer (artifacts\TTSFreeUse-Setup-<version>.exe).

.DESCRIPTION
  1. Publishes the Windows app (TTSFreeUse.Desktop) self-contained, so users
     don't need .NET installed.
  2. Packages it with Inno Setup 6 (install it with: winget install JRSoftware.InnoSetup).

.EXAMPLE
  .\installer\build-installer.ps1 -Version 0.2.0
#>
param(
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "artifacts\publish\win-x64"

Write-Host "Publishing TTSFreeUse $Version..."
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
# IncludeAzure=false: the installer ships only open-source components, so the
# optional Azure engine (closed-source Microsoft Speech SDK) is left out.
$desktop = Join-Path $root "TTSFreeUse.Desktop"
# Restore explicitly with the same settings: switching IncludeAzure between
# builds otherwise leaves a stale package cache behind.
dotnet restore $desktop -r win-x64 "-p:IncludeAzure=false" --force
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }
dotnet publish $desktop -c Release -r win-x64 --self-contained --no-restore `
    -o $publish "-p:Version=$Version" "-p:IncludeAzure=false"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$closedSource = Get-ChildItem $publish -Recurse -Filter "*CognitiveServices*"
if ($closedSource) {
    throw "Closed-source files ended up in the package: $($closedSource.Name -join ', ')"
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $command = Get-Command iscc -ErrorAction SilentlyContinue
    if ($command) { $iscc = $command.Source }
}
if (-not $iscc) {
    Write-Host ""
    Write-Host "The app is published in $publish, but Inno Setup 6 isn't installed, so no installer was made."
    Write-Host "Install it with:  winget install JRSoftware.InnoSetup"
    Write-Host "then run this script again."
    exit 1
}

Write-Host "Building the installer with $iscc..."
& $iscc "/DAppVersion=$Version" "/DPublishDir=$publish" (Join-Path $PSScriptRoot "TTSFreeUse.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

Write-Host ""
Write-Host "Done: $(Join-Path $root "artifacts\TTSFreeUse-Setup-$Version.exe")"
