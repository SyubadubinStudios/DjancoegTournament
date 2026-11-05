# Getting started

*Made by Ariana Mischa Fadhila of Syubadubin Studios.*

## Requirements

- **.NET 10 SDK** (10.0.100 or newer) — `dotnet --version`
- A GPU and driver with **OpenGL 3.3** (any GPU from the last decade, including integrated graphics)
- Windows 10/11, a modern Linux desktop (X11 or Wayland/XWayland), or macOS 12+

Raylib's native libraries for `win-x64`, `win-x86`, `linux-x64`, `osx-x64` and `osx-arm64` come with the Raylib-cs NuGet package, so there is nothing else to install.

## Build, test, run

```bash
git clone <repo> && cd DjancoegTournament
dotnet build                    # builds all 6 projects
dotnet test                     # 93 tests: physics, combat, arenas, bots, networking, mods
dotnet run --project src/Djancoeg.Client
```

For the smoothest frame rate, play a Release build:

```bash
dotnet run --project src/Djancoeg.Client -c Release
```

Run a single test, or a group of tests:

```bash
dotnet test --filter "FullyQualifiedName~MovementTests.Player_climbs_stairs"
dotnet test --filter "FullyQualifiedName~LoopbackTests"
```

## First match

1. **Fighters** (*Pilih jagoan*) — pick a fighter and type your name.
2. **Practice vs bots** (*Latihan lawan bot*) — choose an arena, a mode, the number of bots and their skill.
3. **Start match** (*Gas, mulai!*). Press **Esc** for the pause menu and **Tab** for the scoreboard.

![Practice setup](../screenshots/setup-id.png)

## Client command line

| Option | Meaning |
|---|---|
| `--connect host:port` | Join a server directly on start-up |
| `--lang en` / `--lang id` | Override the interface language |
| `--size 1600x900` | Requested window size (shrinks to fit the monitor) |
| `--novsync` | Disable VSync (benchmarking) |
| `--shot <scene> --out file.png [--arena id] [--delay s]` | Render one scene and save a screenshot (see [assets.md](assets.md)) |

Settings are stored in `%APPDATA%/DjancoegTournament/settings.json` (Windows) or `~/.config/DjancoegTournament/settings.json` (Linux/macOS).

## Release packages

```powershell
pwsh scripts/publish.ps1                       # all platforms -> artifacts/*.zip
pwsh scripts/publish.ps1 -Runtimes win-x64
```
```bash
scripts/publish.sh linux-x64                   # -> artifacts/*.tar.gz
```

Each package is self-contained (no .NET install needed) and contains both the game (`DjancoegTournament`) and the server (`djancoeg-server`).

## Troubleshooting

| Problem | Fix |
|---|---|
| Black window or crash on start | Update your GPU driver; the game needs OpenGL 3.3. On Linux VMs, enable 3D acceleration. |
| "Content mismatch" when joining | The server runs different mods. Enable the same mods in the Workshop (the message lists them). |
| Nobody can join my hosted game | Allow UDP port 27960 in your firewall; for internet play forward UDP 27960 on your router. |
| No sound | Check the volume sliders in Settings; on Linux make sure PulseAudio/PipeWire is running. |
| Mouse is stuck to the window | Press **Esc** to open the pause menu, which releases the cursor. |
