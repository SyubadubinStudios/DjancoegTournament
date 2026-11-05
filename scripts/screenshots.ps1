# Regenerates every screenshot used by README.md and docs/ (needs a desktop session).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet build "$root/src/Djancoeg.Client" -c Release | Out-Null
$exe = Join-Path $root "src/Djancoeg.Client/bin/Release/net10.0/DjancoegTournament.exe"
$shots = Join-Path $root "docs/screenshots"
New-Item -ItemType Directory -Force $shots | Out-Null
$jobs = @(
  @("menu", "id", ""), @("menu", "en", ""), @("fighters", "id", ""), @("fighters", "en", ""), @("setup", "id", ""),
  @("browser", "id", ""), @("workshop", "id", ""), @("settings", "id", ""), @("credits", "id", ""),
  @("game", "id", "pasar-malam"), @("game-tps", "id", "atap-kampung"), @("game", "en", "terminal-bus"),
  @("game-tps", "id", "sawah-ronda"), @("scoreboard", "id", "terminal-bus"), @("game-tps", "en", "lapangan-17an"),
  @("controls", "id", ""), @("controls", "en", ""), @("settings-graphics", "id", ""), @("settings-access", "en", ""),
  @("spectate", "id", "pasar-malam"), @("setup", "en", ""),
  @("game-tps", "id", "pelabuhan", "ctf"), @("game-tps", "en", "stasiun-kereta", "race"), @("game", "en", "stasiun-kereta"), @("game-tps", "en", "kantor-kelurahan")
)
foreach ($j in $jobs) {
  $name = "$($j[0])-$($j[1])" + $(if ($j[2]) { "-$($j[2])" } else { "" })
  if ($j.Count -gt 3) { $name += "-$($j[3])" }
  $args = @("--shot", $j[0], "--lang", $j[1], "--out", (Join-Path $shots "$name.png"))
  if ($j[2]) { $args += @("--arena", $j[2]) }
  if ($j.Count -gt 3) { $args += @("--mode", $j[3]) }
  Start-Process -FilePath $exe -ArgumentList $args -Wait -NoNewWindow
}
