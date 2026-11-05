# Progress — Development tracking / Pelacakan pengembangan

*Djancoeg Tournament · Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.*

Current version / Versi sekarang: **1.1.0** · Roadmap / Peta jalan: [PLAN.md](PLAN.md)

## Summary / Ringkasan

| Metric | Value |
|---|---|
| Projects / Proyek | 6 (Core, Net, Modding, Server, Client, Tests) |
| Automated tests / Tes otomatis | 59 passing / lulus |
| Fighters / Jagoan | 13 built-in + 1 mod, all rigged and animated (idle/run/shoot/die) |
| Weapons / Senjata | 7 built-in + 1 mod |
| Arenas | 4 built-in + 1 mod |
| Modes | 3 |
| Languages / Bahasa | English, Bahasa Indonesia (UI, docs, content) |
| Performance | ~200 FPS (Release, VSync off, 1366×768 integrated GPU, 8 fighters); zero allocations per simulation tick |

## Log

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
- No packet encryption or NAT punch-through yet (planned for v1.3).
- Stereo pan direction depends on the raylib audio backend's pan convention.

## Next up / Berikutnya

See v1.2 "Pasar Rakyat" in [PLAN.md](PLAN.md): new arenas (Pelabuhan, Stasiun Kereta, Kantor Kelurahan), fighters, weapons and modes.
