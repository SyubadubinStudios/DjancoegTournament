# Uploads a published build folder to itch.io with butler.
# Usage: pwsh distribution/itch/push.ps1 -Channel windows -Folder artifacts/DjancoegTournament-win-x64 [-Target user/djancoeg-tournament] [-Version v2.0.0]
# Needs BUTLER_API_KEY in the environment (or a prior `butler login`).
param(
    [Parameter(Mandatory)][string]$Channel,
    [Parameter(Mandatory)][string]$Folder,
    [string]$Target = $(if ($env:ITCH_TARGET) { $env:ITCH_TARGET } else { "syubadubin/djancoeg-tournament" }),
    [string]$Version = ""
)
$ErrorActionPreference = "Stop"
$butler = Get-Command butler -ErrorAction SilentlyContinue
if (-not $butler) {
    $os = if ($IsWindows) { "windows-amd64" } elseif ($IsMacOS) { "darwin-amd64" } else { "linux-amd64" }
    $zip = Join-Path ([IO.Path]::GetTempPath()) "butler.zip"
    $dir = Join-Path ([IO.Path]::GetTempPath()) "butler"
    Invoke-WebRequest "https://broth.itch.zone/butler/$os/LATEST/archive/default" -OutFile $zip
    Expand-Archive $zip -DestinationPath $dir -Force
    $exe = Join-Path $dir $(if ($IsWindows) { "butler.exe" } else { "butler" })
    if (-not $IsWindows) { chmod +x $exe }
} else { $exe = $butler.Source }
$args = @("push", $Folder, "${Target}:$Channel")
if ($Version) { $args += @("--userversion", $Version.TrimStart("v")) }
& $exe @args
if ($LASTEXITCODE -ne 0) { throw "butler push failed ($LASTEXITCODE)" }
