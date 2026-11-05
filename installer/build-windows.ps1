# Builds the Windows installer (Inno Setup 6) and the portable zip.
# Usage: pwsh installer/build-windows.ps1 [-Version 2.1.0] [-SkipPublish]
# Code signing (optional): set WINDOWS_CERT_PFX_BASE64 + WINDOWS_CERT_PASSWORD (a code-signing .pfx). The game
# executables, the portable zip contents and the setup are then Authenticode-signed with an RFC 3161 timestamp.
param([string]$Version = "", [switch]$SkipPublish)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) { $Version = ([xml](Get-Content "$root/Directory.Build.props")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
$rid = "win-x64"
$src = Join-Path $root "artifacts/DjancoegTournament-$rid"
$out = Join-Path $root "artifacts/installers"
New-Item -ItemType Directory -Force $out | Out-Null
if (-not $SkipPublish) { & "$root/scripts/publish.ps1" -Runtimes $rid }

$pfx = $null
if ($env:WINDOWS_CERT_PFX_BASE64) {
    $pfx = Join-Path ([IO.Path]::GetTempPath()) "djancoeg-signing.pfx"
    [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:WINDOWS_CERT_PFX_BASE64))
}
function Sign-Files([string[]]$files) {
    if (-not $pfx) { return }
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw "signtool.exe not found (install the Windows SDK)." }
    & $signtool.FullName sign /f $pfx /p $env:WINDOWS_CERT_PASSWORD /fd sha256 /tr http://timestamp.digicert.com /td sha256 /d "Djancoeg Tournament" @files
    if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE)" }
}
if ($pfx) {
    Sign-Files @((Join-Path $src "DjancoegTournament.exe"), (Join-Path $src "djancoeg-server.exe"))
    Compress-Archive -Path "$src/*" -DestinationPath "$src.zip" -Force   # portable zip with signed executables
}

$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    foreach ($p in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $p) { $iscc = $p; break }
    }
}
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found. Install it: winget install JRSoftware.InnoSetup  or  choco install innosetup" }

& $iscc "/DAppVersion=$Version" "/DSourceDir=$src" "/DOutputDir=$out" (Join-Path $PSScriptRoot "windows/DjancoegTournament.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
Sign-Files @((Join-Path $out "DjancoegTournament-Setup-win-x64.exe"))
if ($pfx) { Remove-Item $pfx -Force; Write-Host "Signed with Authenticode." } else { Write-Host "Not signed (no WINDOWS_CERT_PFX_BASE64)." }
Copy-Item "$src.zip" (Join-Path $out "DjancoegTournament-win-x64-portable.zip") -Force
Write-Host "Windows installer: $out"
