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
dotnet publish (Join-Path $root "TTSFreeUse.Desktop") -c Release -r win-x64 --self-contained `
    -o $publish "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

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
