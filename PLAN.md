# PLAN — Development roadmap / Peta jalan pengembangan

*Djancoeg Tournament · Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.*

Status legend / Keterangan: ✅ done/selesai · 🚧 in progress/dikerjakan · ⬜ planned/direncanakan

## v1.0 — "Tawuran Perdana" (first playable release / rilis pertama) ✅

| Area | Scope (EN) | Cakupan (ID) | Status |
|---|---|---|---|
| Core | 60 Hz authoritative simulation, Quake-style movement, swept AABB collision, zero-alloc tick | Simulasi otoritatif 60 Hz, gerak gaya Quake, tabrakan swept AABB, tick tanpa alokasi | ✅ |
| Combat | 7 weapons (melee, hitscan, pellets, projectiles, bouncing, splash, piercing), headshots, armor, Jamu ×3, rocket jumps | 7 senjata, kena kepala, zirah, Jamu ×3, rocket jump | ✅ |
| Fighters | 13 fighters in 5 categories with 7 ability kinds | 13 jagoan dalam 5 kategori dengan 7 jenis jurus | ✅ |
| Arenas | Night Market, Bus Terminal, Rice Fields, Kampung Rooftops | Pasar Malam, Terminal, Sawah, Atap Kampung | ✅ |
| Modes | Free-for-All, Team Deathmatch, King of the Warung | Tawuran Bebas, Perang Kampung, Raja Warung | ✅ |
| Chaos | Knockback, kickable damaging props, jump pads, kill planes with kill credit | Dorongan, properti yang bisa ditendang, kasur lompat, kecebur kali | ✅ |
| Bots | Targeting, weapon choice, leading, strafing, pickups, obstacle avoidance | Bot lengkap | ✅ |
| Netcode | UDP, 30 Hz snapshots, prediction + reconciliation, interpolation, lag compensation, reliable events | Netcode lengkap | ✅ |
| Multiplayer | Practice, listen server, LAN discovery, master server + online browser, direct IP, dedicated server | Semua cara bermain | ✅ |
| Client | raylib renderer, single-shader arena, rigged fighters, effects, synthesized audio, voice lines | Renderer, efek, audio | ✅ |
| UI/UX | "Terpal & Running Text" identity, plate HUD, keyboard+mouse menus, EN/ID | Identitas visual, HUD pelat nomor, dua bahasa | ✅ |
| Modding | JSON fighters/weapons/arenas, validation, `.djmod` pack/install, Workshop, example mod | Modding lengkap + contoh mod | ✅ |
| Assets | Rodin portraits/key art/props/voice, Blender sculpt+rig+animate pipeline | Aset Rodin & Blender | ✅ |
| Quality | 57 automated tests, bilingual docs, screenshots, publish scripts | Tes, dokumentasi, screenshot, skrip rilis | ✅ |

## v1.1 — "Ronda Malam" (polish & feel) ✅

- ✅ Rendered arena thumbnails in the setup screen / *thumbnail arena hasil render*
- ✅ Key rebinding and gamepad support (gameplay + menus) / *atur ulang tombol dan dukungan gamepad*
- ✅ Per-weapon crosshair styles and hit sounds (headshot tink) / *crosshair per senjata dan bunyi kena*
- ✅ Footstep sounds by surface kind; crouching is silent / *suara langkah sesuai permukaan; jongkok tanpa suara*
- ✅ Spectator mode (follow + free camera, protocol v2) and kill cam / *mode penonton dan kill cam*
- ✅ Graphics options: effects quality, render scale, blob shadows, MSAA / *opsi grafis*
- ✅ Accessibility: colour-blind team colours, subtitles, reduce shake, reduce flashes / *aksesibilitas*

## v1.2 — "Pasar Rakyat" (content) ✅

- ✅ Arenas: Pelabuhan (harbour), Stasiun Kereta, Kantor Kelurahan / *arena baru*
- ✅ Fighters: Si Kancil (teleport), Leak (levitate), Tukang Parkir (pull), Bapak-Bapak Ronda (team rally) / *jagoan baru*
- ✅ Weapons: Cobek Lempar, Kipas Sate (wind push + projectile deflect), Kerupuk Shuriken; shared weapon slots / *senjata baru*
- ✅ Modes: Rebutan Gorengan (capture the flag), Panjat Pinang (race to the top) / *mode baru*
- ✅ Rodin-generated props: plastic chair, scooter, jerrycan (decimated in Blender) / *properti Rodin*

## v1.3 — "Kampung Online" (online services) ✅

- ✅ NAT punch-through via the master server / *NAT punch-through*
- ✅ Packet encryption (ECDH P-256 + AES-256-GCM, replay window) and stateless connect cookies + server passwords / *enkripsi dan cookie koneksi*
- ✅ Server admin console, rcon (`!login`), kick/ban, map/mode/restart votes / *konsol admin, rcon, voting*
- ✅ Online Workshop index (HTTP) with verified download from the in-game browser / *Workshop online*
- ✅ Delta-compressed snapshots; 32 players per server / *snapshot delta, 32 pemain*
- ✅ Persistent career stats and leaderboards / *statistik dan papan peringkat*
- ✅ Bonus: realistic procedural skybox with lit clouds, stars and optional HDRI panoramas / *langit realistis*

## v1.4 — "Makhluk Aneh" (creeps & effects) ✅

- ✅ 8 Arena Creeps with random spawn time/place, chase AI (walkers, flyer, burrowing worm), killable, rewards, toggle (default on)
- ✅ Creature models built in Blender, voice lines from Rodin/ElevenLabs, synthesized creature sounds
- ✅ Blood/gore with floor and wall splats (confetti option), bullet holes, impact dust, muzzle smoke, layered explosions

## v2.0 — "Nusantara Raya" ✅

- ✅ Level editor inside the game: fly camera, 7 tools, undo/redo, save as mod, F5 test play / *editor arena di dalam game*
- ✅ Physics corpses ("ragdoll-lite") and destructible stalls/benches/counters/water tanks that respawn (protocol v6) / *mayat berfisika dan lapak yang bisa hancur*
- ✅ Tur Nusantara: 4 islands × 4 stages, stars, unlocks, boss fights, island map art / *tur pemain tunggal antar pulau*
- ✅ Distribution: GitHub Actions CI + tagged releases, itch.io butler, Steam ContentBuilder templates; 12 local achievements with Steam mapping / *distribusi dan pencapaian*

## v2.1 — "Timur Jauh" (installers & bonus island) ✅

- ✅ Installers for every platform: Windows (Inno Setup), Linux (AppImage, .deb, tar.gz), macOS (.app in .dmg, Apple Silicon + Intel); release workflow with checksums / *installer untuk semua platform*
- ✅ Installed builds keep mods/editor arenas in the user folder (read-only install dirs) / *mod di folder pengguna*
- ✅ Nusa Tenggara bonus island with two bosses (Si Jangkung, Giant Earthworm); bosses keep their damage when they come back / *pulau bonus Nusa Tenggara*
- ✅ Breakage chunks that tumble and rest until the stall respawns / *bongkahan puing*
- ✅ Editor: rotate 90°, sky presets, export `.djmod` / *editor: putar, suasana langit, ekspor*

## v2.2 — "Bhinneka" (story, co-op, physics) ✅

- ✅ Per-limb ragdolls for fighters (Verlet joints, rigid torso, hinge limits, arena collision) / *ragdoll per anggota tubuh*
- ✅ Partial destruction: breakable boxes split into up to 4 pieces; 128-bit destroyed mask (protocol v7) / *kehancuran sebagian*
- ✅ Editor: drag gizmos (move/resize, one undo per drag), copy/paste between arenas, open saved/built-in arenas (F6) / *editor: gizmo, salin antar arena*
- ✅ Campaign: illustrated story stills per island + finale (Rodin), LAN co-op (humans vs bots) / *gambar cerita, main bareng LAN*

## v2.3 — "Gotong Royong" (play together, ship everywhere) ✅

- ✅ Creep ragdolls (shared rig); tall breakables split into layers that collapse and come back bottom-first / *ragdoll makhluk, lapisan runtuh*
- ✅ Editor multi-select; Workshop upload with per-uploader tokens (`POST /upload`, ownership, validation) / *pilih banyak, unggah Workshop*
- ✅ Online co-op through the master server; co-op guests earn stars (protocol v8 sends the stage); narrated story lines (ElevenLabs) / *main bareng online, tamu dapat bintang, narasi*
- ✅ Signing hooks (Authenticode, Developer ID + notarization), Flatpak bundle, winget + Homebrew manifests per release, Steamworks behind `-p:Steam=true` / *tanda tangan, Flatpak, winget/Homebrew, Steam*

## Ideas after 2.3 / Ide setelah 2.3

- Real code-signing certificates and a Steam app id (owner accounts needed), then submit winget/Homebrew/Flathub
- English narration track; voiced barks for bosses
- Workshop web page (browse/rate) and moderation tools
- Linux/macOS Steam build (Steamworks native libraries for those platforms)

## Technical principles / Prinsip teknis

1. One authoritative simulation for every mode of play. / *Satu simulasi otoritatif untuk semua cara bermain.*
2. No allocation in the tick; measure it in tests. / *Tanpa alokasi di tick; diukur di tes.*
3. Content is data; anything a mod can't express is a bug in the format. / *Konten adalah data.*
4. Every feature ships with EN + ID text and docs. / *Setiap fitur hadir dalam dua bahasa.*
