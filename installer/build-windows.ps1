# Builds the Windows installer (Inno Setup 6) and the portable zip.
# Usage: pwsh installer/build-windows.ps1 [-Version 2.1.0] [-SkipPublish]
param([string]$Version = "", [switch]$SkipPublish)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) { $Version = ([xml](Get-Content "$root/Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
$rid = "win-x64"
$src = Join-Path $root "artifacts/DjancoegTournament-$rid"
$out = Join-Path $root "artifacts/installers"
New-Item -ItemType Directory -Force $out | Out-Null
if (-not $SkipPublish) { & "$root/scripts/publish.ps1" -Runtimes $rid }

$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    foreach ($p in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $p) { $iscc = $p; break }
    }
}
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found. Install it: winget install JRSoftware.InnoSetup  or  choco install innosetup" }

& $iscc "/DAppVersion=$Version" "/DSourceDir=$src" "/DOutputDir=$out" (Join-Path $PSScriptRoot "windows/DjancoegTournament.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
Copy-Item "$src.zip" (Join-Path $out "DjancoegTournament-win-x64-portable.zip") -Force
Write-Host "Windows installer: $out"
