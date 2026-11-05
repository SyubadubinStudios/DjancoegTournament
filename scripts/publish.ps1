# Builds self-contained release packages for Windows, Linux and macOS.
# Usage: pwsh scripts/publish.ps1 [-Runtimes win-x64,linux-x64,osx-arm64]
param([string[]]$Runtimes = @("win-x64", "linux-x64", "osx-x64", "osx-arm64"))
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts"
foreach ($rid in $Runtimes) {
    $dest = Join-Path $out "DjancoegTournament-$rid"
    Write-Host "== $rid -> $dest"
    dotnet publish "$root/src/Djancoeg.Client" -c Release -r $rid --self-contained true -p:PublishSingleFile=false -o $dest
    dotnet publish "$root/src/Djancoeg.Server" -c Release -r $rid --self-contained true -p:PublishSingleFile=true -o $dest
    # itch.io launch manifest with this platform's executable names.
    $ext = if ($rid.StartsWith("win")) { ".exe" } else { "" }
    (Get-Content (Join-Path $root "distribution/itch/.itch.toml") -Raw).Replace("{{EXE}}", "DjancoegTournament$ext").Replace("{{SERVER}}", "djancoeg-server$ext") |
        Set-Content -Path (Join-Path $dest ".itch.toml") -Encoding utf8
    Compress-Archive -Path "$dest/*" -DestinationPath "$dest.zip" -Force
}
Write-Host "Done. Packages are in $out"
