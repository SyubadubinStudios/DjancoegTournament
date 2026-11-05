# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

**Djancoeg Tournament — Ultimate Indonesian Chaos Battle Arena**: a .NET 10 multiplayer FPS/TPS arena shooter (raylib via Raylib-cs) with Indonesian folk/meme characters. The original brief is `DjancoegTournament.txt` (Indonesian) + `solution-design.md`. Roadmap in `PLAN.md`, tracking in `Progress.md` — update both when you finish meaningful work.

## Commands

```bash
dotnet build                                            # whole solution (DjancoegTournament.sln)
dotnet test                                             # all xUnit tests
dotnet test --filter "FullyQualifiedName~MovementTests.Player_climbs_stairs"   # single test
dotnet run --project src/Djancoeg.Client -c Release     # the game
dotnet run --project src/Djancoeg.Server -- --bots 6 --arena pasar-malam       # dedicated server
dotnet run --project src/Djancoeg.Server -- mod validate mods/warung-bakso     # mod tools (new/validate/pack/install/list)
src/Djancoeg.Client/bin/Release/net10.0/DjancoegTournament.exe --shot game-tps --arena atap-kampung --out x.png   # render a screenshot
pwsh scripts/screenshots.ps1                            # regenerate docs/screenshots
pwsh scripts/publish.ps1                                # self-contained packages -> artifacts/
```

The GUI exe is `WinExe`: from PowerShell use `Start-Process -Wait` or it returns immediately. The dev monitor is 1366×768; the window auto-fits.

## Architecture (big picture)

- `Djancoeg.Core` — pure simulation, no rendering. `GameWorld` (partials: `.cs` players/tick, `.Combat.cs` weapons/damage/abilities, `.World.cs` props/pickups/rules) steps at 60 Hz over struct arrays. `PlayerMovement.Step` is shared by server and client prediction — any change affects both and must stay deterministic given the same inputs. All damage goes through `GameWorld.ApplyDamage`. `EventBuffer` events drive client effects/sounds/kill feed and are replicated.
- Content is data: `WeaponDef`/`CharacterDef`/`ArenaDef` (`Content/Definitions.cs`), built-ins in `BuiltInContent.cs` and `BuiltInArenas.cs` (procedural via `ArenaBuilder`; `PadTo` solves jump-pad launch velocities). `ContentRegistry` indices go over the wire, so content order must match on server/client; `Signature` guards it.
- `Djancoeg.Modding` — `mod.json` (`ModManifest`) + source-generated `ModJsonContext`. **Content properties must use `{ get; set; }`, not `init`** — STJ source-gen resets missing init props to default (regression test exists).
- `Djancoeg.Net` — `GameServer` (input queue, 30 Hz snapshots within 1200 bytes, acked events, lag compensation via `GameWorld.HistoricalPosition`), `GameClient` (prediction/reconciliation, interpolation via `Bracket`/`RenderTick`), `ServerBrowser` (LAN + master), `MasterServer`. Practice/host run a `GameServer` thread inside the client (`Client/Game/Session.cs`), so every mode uses the same path.
- `Djancoeg.Client` — `App` (window, shared `WorldShader`/`ModelLibrary`/`SoundBank`/`Gui`, screen switching via `App.Go`), `Screens/*`, `Game/GameView(.Hud).cs` (3D + HUD), `Render/*` (single world shader with procedural surface patterns keyed by texcoord.x = `SurfaceKind`), `Audio/SoundBank.cs` (synthesized SFX), `Ui/Theme.cs` + `Ui/Gui.cs` (immediate-mode widgets; widgets are called from `Draw()`).

## Rules that are easy to break

- `GameWorld.Step()` must not allocate (`BotMatchTests.Simulation_tick_does_not_allocate`).
- Every user-facing string needs EN + ID: UI copy in `Client/Strings.cs` (`S.T(key)`), content via `LocalizedText`. Docs exist in `docs/en` and `docs/id` — keep both in sync.
- Required credit line must stay visible in app and docs: "Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios" (`GameInfo.CreditId`).
- Fonts load Latin-1 only (codepoints 32–255); avoid characters like `›` or `∞` in UI text.
- UI follows the "Terpal & Running Text" identity in `Ui/Theme.cs` (tarp blue, angkot yellow, chili red, chalk, asphalt; Bungee / Plus Jakarta Sans / Space Mono). Use the project `frontend-design` skill for new UI.
- No real people or brands in content or generated art.

## Assets

- Fighters: `tools/blender/build_characters.py` (run via Blender MCP `execute_blender_code` with `exec(open(path).read(), g)`, or `blender -b --python ... -- all`). Shared 12-bone rig, clips `idle/run/shoot/die` as NLA tracks, vertex colours as raw sRGB FLOAT_COLOR, face −Y. Game loads `assets/models/characters/<id>.glb` and `assets/art/portraits/<id>.png` by id.
- Don't call `bpy.ops.wm.read_factory_settings` through the MCP — it unloads the MCP addon.
- Rodin MCP (`.mcp.json`) generates images, 3D props (decimate in Blender before use) and voice lines; parallel voice requests tend to fail, send them one at a time.
