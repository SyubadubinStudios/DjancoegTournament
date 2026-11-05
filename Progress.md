# Progress — Development tracking / Pelacakan pengembangan

*Djancoeg Tournament · Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.*

Current version / Versi sekarang: **2.2.0** · Roadmap / Peta jalan: [PLAN.md](PLAN.md)

## Summary / Ringkasan

| Metric | Value |
|---|---|
| Projects / Proyek | 6 (Core, Net, Modding, Server, Client, Tests) |
| Automated tests / Tes otomatis | 138 passing / lulus |
| Fighters / Jagoan | 17 built-in + 1 mod, all rigged and animated (idle/run/shoot/die) |
| Weapons / Senjata | 10 built-in + 1 mod |
| Arenas | 7 built-in + 1 mod |
| Modes | 5 + campaign (20 stages, 6 bosses) |
| Packages / Paket | Windows installer + zip, Linux AppImage + .deb + tar.gz, macOS .dmg (arm64, x64) |
| Achievements / Pencapaian | 12 |
| Languages / Bahasa | English, Bahasa Indonesia (UI, docs, content) |
| Performance | ~200 FPS (Release, VSync off, 1366×768 integrated GPU, 8 fighters); zero allocations per simulation tick |

## Log

### 2026-11-06 — v2.2.0 "Bhinneka"

- [x] Per-limb ragdolls: `Core/Simulation/Ragdoll.cs` (15 Verlet joints for the shared 12-bone rig, rigid torso cluster, hinge limits, swept arena collision, sleeping) driven into the skinned model through a one-frame synthetic `ModelAnimation`; rigid fallback for creeps and unknown rigs
- [x] Partial destruction: `ArenaPieces` splits breakable boxes into up to 4 pieces when an arena enters the registry (same on server and client); `MaxDestructibles` 128 with a `UInt128` mask, protocol v7
- [x] Editor: X/Y/Z drag gizmos (move, Ctrl+drag resize, one undo per drag via gestures), Ctrl+C/Ctrl+V clipboard shared across arenas, F6 opens saved arenas and editable copies of built-in/mod arenas
- [x] Campaign story: 6 illustrated stills (Rodin) with EN/ID captions, typewriter `CutsceneScreen` with Ken Burns pan, island intros once + finale, **Story** replay button
- [x] Co-op over LAN: `MatchSettings.CoopHumans` puts humans on team 0 vs bots; FFA stages become humans-vs-bots, bosses +50% health; host records progress
- [x] Tests: ragdoll settling/shape/pose, piece splitting/registry/neighbours/128-bit mask, drag undo, cross-arena paste, built-in copies, co-op teams, story assets (138 tests)

### 2026-11-06 — v2.1.0 "Timur Jauh"

- [x] Installers: Inno Setup (Windows, per-user, shortcuts for game/server/mods, optional firewall rules), AppImage + .deb + tar.gz (Linux), ad-hoc-signed .app in .dmg for Apple Silicon and Intel (macOS); `release.yml` builds all of them and publishes a GitHub Release with `SHA256SUMS.txt`
- [x] Installed (read-only) builds use `<AppData>/DjancoegTournament/mods`, seeded with the bundled mods; `DJANCOEG_MODS` override; `InvariantGlobalization` removes the ICU dependency
- [x] Nusa Tenggara bonus island (4 stages, bosses Si Jangkung and Giant Earthworm); bosses keep their remaining health when they leave and return
- [x] Breakage chunks for destroyed boxes (tumble, rest until respawn)
- [x] Editor: rotate 90° (R), sky presets (F3), export `.djmod` (Ctrl+E)
- [x] README download section (EN/ID) with stable latest-release links; installer docs in getting-started (EN/ID)
- [x] Tests: rotate, environment presets, user mods folder seeding, boss health carry-over, island 5 (127 tests)

### 2026-11-06 — v2.0.0 "Nusantara Raya"

- [x] Destructible arena boxes: `ArenaBox.Health`, `CollisionWorld.SetBoxEnabled` + box-index raycasts, damage from hitscan/melee/projectiles/explosions, 45 s respawn (blocked while occupied), protocol v6 `DestroyedMask` mirrored into client prediction; built-in breakables in 4 arenas; splinters/dust/crunch
- [x] Physics corpses ("ragdoll-lite") for fighters and creeps: thrown by the kill impulse, collide/bounce with the arena, blood splats, settle and fade
- [x] Tur Nusantara campaign: 4 islands × 4 stages across all modes, 1–3 star rules, stage/island unlocks, boss stages (scaled creep that respawns, +10 killing blow, match ends on defeat) with boss bar and stage banner; island map art via Rodin; progress in AppData
- [x] In-game level editor: window-free `ArenaEditor` (place/move/resize/style/duplicate/delete, grid, undo/redo, ray picking, validation, save as `arena-buatanku` mod) + `EditorScreen` (fly camera, tool panel, F5 test play)
- [x] 12 local achievements with toasts and a screen; screenshot runs never touch real saves
- [x] Distribution: GitHub Actions CI (Windows + Linux tests, tagged packages/releases, optional itch.io butler push), `.itch.toml`, Steam ContentBuilder templates, Steam achievement mapping
- [x] Docs EN/ID: campaign, editor, destruction, achievements, distribution; new screenshots (campaign, boss, editor, achievements)
- [x] Tests: destruction, campaign data/stars/unlocks, boss flow, boss-stage zero-allocation tick, editor operations/picking/saving (122 tests)

**Fixed during v2.0 / Diperbaiki selama v2.0**

- Boss lookup allocated a closure every tick → boss creep index resolved once per match (allocation test added).
- Breakable boxes were drawn unlit with `DrawCubeV` → baked per-box meshes through the world shader.
- Campaign map used a ★ glyph missing from the Latin-1 fonts → stars are drawn as shapes.
- Level-editor camera started facing away from the arena.

### 2026-11-06 — v1.4.0 "Makhluk Aneh"

- [x] Arena Creeps: `CreepDef` content (mod-extensible), random spawner, per-shape AI (walkers via `PlayerMovement`, flying head, burrowing worm with rumble warnings and eruptions), attacks, damage from every weapon type, rewards, bots fight creeps, protocol v5 replication, `--no-creeps`, Settings/Setup toggle (default on)
- [x] Original creature designs in place of copyrighted references (no E.T., Titans, Dune worm or Mommy Long Legs): Wedding-Party Zombies, Giant Floating Head, the Lanky One, Lost Aliens, Giant Albino Gorilla, Buto Ijo, Buff Cyclops, Giant Earthworm
- [x] 7 rigged creature models via Blender MCP (worm drawn procedurally), 4 voice lines via Rodin/ElevenLabs, 9 synthesized creature/siren/splat sounds
- [x] Effects: blood hits with floor/wall splats, gore and gibs, bullet-hole decals, impact dust, muzzle smoke, explosion smoke column + shockwave ring + debris + scorch; confetti mode toggle
- [x] Tests: creeps appear/disable, every creep chases/hurts/dies, creep kills cost no points, slingshot hits creeps, zero-allocation tick with creeps (105 tests)


### 2026-11-06 — v1.3.0 "Kampung Online"

**Done / Selesai**

- [x] Realistic procedural sky (requested mid-milestone): atmosphere gradient, Mie sun halo, sun/moon disc, stars, FBM clouds lit from the sun, per-arena time-of-day palettes, optional equirect `skyTexture`
- [x] Protocol v4: stateless HMAC connect cookies (anti-spoofing/amplification), password proof, ECDH P-256 + HKDF + AES-256-GCM channel with 64-packet replay window
- [x] Delta-compressed snapshots (per-player field masks against acknowledged baselines); 1400-byte budget; up to 32 players
- [x] Admin: console on stdin, `!login` rcon, local admins on listen servers; status/kick/ban/unban/map/mode/bots/skill/restart/say/password/top; bans persisted
- [x] Votes: `!vote map|mode|restart`, F1/F2 banner, majority of humans within 30 s
- [x] NAT punch-through via the master (PunchRequest → PunchNotify → Punch)
- [x] Career stats (`StatsStore`, atomic JSON) and leaderboards: in-match server board in the pause menu, local board in the main menu
- [x] Online Workshop: `workshop publish/serve` (read-only HTTP, traversal-safe), in-game Online tab with SHA-256 verified install/update
- [x] Client: password field, password-protected markers, reject reasons (password/banned/kicked), host password, Workshop URL setting
- [x] Tests: crypto (round trip, tamper, replay, window, cookies), password handshake, 32-player deltas, admin/ban, map + vote, rcon, stats/leaderboard, NAT punch, Workshop publish/serve/install (93 tests)

**Known limitations / Keterbatasan yang diketahui**

- The key exchange has no server certificate, so an active man-in-the-middle could still intercept a session.
- Symmetric NATs can't be punched; forward the port instead.
- Workshop packages aren't signed; the SHA-256 only proves the download matches the index you trust.


### 2026-11-06 — v1.2.0 "Pasar Rakyat"

**Done / Selesai**

- [x] Arenas: Pelabuhan Sunda Kelapa (containers, pinisi with crow's nest, channel kill plane), Stasiun Kereta Tua (carriage roofs, footbridge, clock tower), Kantor Kelurahan (interior rooms, counters, walkable roof)
- [x] Modes: Rebutan Gorengan (capture the flag: take/drop/return/capture, 20 s auto-return, bot objectives) and Panjat Pinang (race to the goal, respawn on score)
- [x] Weapons: Cobek Lempar (heavy lob), Kipas Sate (new `Blast` kind: knockback cone that deflects projectiles), Kerupuk Shuriken (3-way); weapons sharing a key cycle
- [x] Fighters: Si Kancil (Teleport), Leak (Levitate, predicted on the client), Tukang Parkir (Pull), Bapak-Bapak Ronda (Rally: team heal + speed boost)
- [x] Protocol v3: gorengan state in snapshots, levitation flag; client mirrors Rally/Haste speed rules
- [x] Assets: 4 Rodin portraits, Rodin chair/scooter/jerrycan props (decimated in Blender), 4 new Blender fighters (new `deer` and `leak` silhouettes)
- [x] Client: gorengan, plates and race-goal beacon, objective HUD with off-screen goal marker, wind/teleport/rally/deflect effects, 5 new synthesized sounds, fighter grid that fits any roster size
- [x] Server CLI: `--mode ctf|race`; arena list updated
- [x] Tests: 9 new mechanic tests; new arenas covered by the arena and bot-match suites (80 tests)
- [x] Docs EN/ID, README, screenshots (25)


### 2026-11-05 — v1.1.0 "Ronda Malam"

**Done / Selesai**

- [x] Repository moved to `C:\experiment\syubadubin\DjancoegTournament` (GitHub `SyubadubinStudios/DjancoegTournament`)
- [x] `Controls`: rebindable keyboard/mouse actions saved in settings, Controls screen with capture + reset, gamepad gameplay (sticks, triggers, face buttons) and gamepad menu navigation
- [x] Per-weapon crosshairs (melee circle, true-spread ring, piercing ticks, drop marks, splash ring); headshot hit sound; pitch per weapon
- [x] Surface footsteps (7 synthesized sounds, `CollisionWorld.SurfaceBelow`), silent while crouching
- [x] Spectator mode: protocol v2 spectator flag, up to 8 spectators without a player slot, follow camera with cycling, free-fly camera, spectator HUD; *Watch* in browser and *Watch only* for practice
- [x] Kill cam: looks at your killer and shows their HP until respawn
- [x] Graphics tab: effects quality (lights/particles), 3D render scale via render target, blob shadows, MSAA toggle
- [x] Accessibility tab: colour-blind (Okabe-Ito) team colours, announcer subtitles, reduce shake, reduce flashes
- [x] Rendered arena thumbnails in the setup screen
- [x] Settings split into General / Graphics / Accessibility / Controls tabs
- [x] Tests: spectator loopback, surface lookup; network tests serialized to remove cold-start flakiness (59 tests)
- [x] Docs EN/ID and screenshots updated (21 screenshots)


### 2026-11-05 — v1.0.0

**Done / Selesai**

- [x] Solution, shared build settings (`Directory.Build.props`), analyzers, AOT-compatible libraries
- [x] Core: geometry, PRNG, bilingual `LocalizedText`, content definitions, `ContentRegistry` + content signature
- [x] Arenas generated with `ArenaBuilder` (stairs, landing-solved jump pads): Pasar Malam, Terminal Antar Kota, Sawah & Pos Ronda, Atap Kampung
- [x] Movement: Quake-style accel/friction, swept AABB, sub-steps, stairs, ledge snap, crouch, jump pads
- [x] Combat: melee, hitscan (spread, pellets, piercing, headshots, lag compensation), projectiles (gravity, bounce, splash), armor, Jamu, knockback
- [x] Abilities: SuperJump, Dash (with contact damage), Shockwave, Heal, Invisibility, Haste, StoneSkin
- [x] Chaos props with damage + kill credit; kill planes with last-attacker credit
- [x] Rules: FFA, TDM (team balance, friendly fire option), KOTH; streaks; match end + restart/rotation
- [x] Bot AI
- [x] Networking: protocol, server (input queue, 1200-byte snapshot budget, reliable events, timeouts, map rotation), client (prediction, reconciliation, interpolation, RTT), LAN discovery, master server, chat
- [x] Dedicated server CLI + master server + mod tools
- [x] Modding: manifest, validation, apply/override, enable/disable, pack/install with path-traversal protection, scaffold; example mod `warung-bakso` with a new fighter, weapon and arena
- [x] Client: window/screen system, single-shader world renderer with procedural surfaces + 16 dynamic lights + fog, baked arena meshes, skinned fighters, prop models, procedural weapons/pickups/projectiles, particles/tracers/flashes, FPS + TPS cameras with wall clipping, viewmodel
- [x] UI/UX via `frontend-design`: "Terpal & Running Text" identity — tarp backdrop with string lights, sticker menus, license-plate HUD, LED running-text marquee, sign-painter shouts; menus for practice, host, fighters, browser, workshop, settings, credits, pause
- [x] Audio: synthesized SFX with positional pan/attenuation; Rodin voice lines (*Tawuran dimulai*, *Mantap jiwa*, *Ngamuk*, *Sakti mandraguna*)
- [x] Assets: Rodin key art, 14 portraits, icon, 3 props (decimated in Blender); Blender pipeline building 14 rigged/animated fighters
- [x] Credits line in app (footer, credits screen, window title tooling) and in every doc
- [x] Docs EN + ID (README, getting started, gameplay, multiplayer, modding, architecture, assets) with screenshots
- [x] Scripts: `publish.ps1` / `publish.sh` (self-contained win/linux/mac), `screenshots.ps1`

**Fixed during development / Diperbaiki selama pengembangan**

- Source-generated JSON reset missing `init` properties to defaults → content types use `set`; regression test added.
- Animation clips inherited channels from each other in Blender NLA export → every keyframe stores a full pose.
- Window larger than small monitors → window auto-fits the monitor; screenshots use an undecorated window.
- Night arena lighting too purple → neutral light floor in the world shader.
- Rodin key art first draft contained a real company's brand name → regenerated with explicit no-text/no-brand constraints.

**Known limitations / Keterbatasan yang diketahui**

- Bots navigate by steering and feelers (no navmesh), so they rarely use stairs to upper walkways — they use jump pads.
- Props are not client-predicted; standing on a moving prop can jitter slightly.
- Corpses are a single rigid body (no per-limb ragdoll), and stalls break as whole boxes.
- Stereo pan direction depends on the raylib audio backend's pan convention.

## Next up / Berikutnya

v2.0 is complete. Ideas for later releases are listed under "Ideas after 2.0" in [PLAN.md](PLAN.md).
