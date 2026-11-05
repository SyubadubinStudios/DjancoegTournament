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

## v1.3 — "Kampung Online" (online services) ⬜

- ⬜ NAT punch-through via the master server / *NAT punch-through*
- ⬜ Packet encryption and connect tokens / *enkripsi paket dan token sambungan*
- ⬜ Server admin console (kick, ban, map vote) / *konsol admin server*
- ⬜ Online Workshop index with download from the in-game browser / *Workshop online*
- ⬜ Delta-compressed snapshots for larger player counts (32+) / *snapshot delta untuk 32+ pemain*
- ⬜ Persistent stats and leaderboards / *statistik dan papan peringkat*

## v2.0 — "Nusantara Raya" ⬜

- ⬜ Level editor inside the game / *editor arena di dalam game*
- ⬜ Ragdoll physics and destructible stalls / *ragdoll dan tenda yang bisa hancur*
- ⬜ Story-lite single-player tour across islands / *tur pemain tunggal antar pulau*
- ⬜ Steam/itch.io distribution, achievements / *distribusi dan pencapaian*

## Technical principles / Prinsip teknis

1. One authoritative simulation for every mode of play. / *Satu simulasi otoritatif untuk semua cara bermain.*
2. No allocation in the tick; measure it in tests. / *Tanpa alokasi di tick; diukur di tes.*
3. Content is data; anything a mod can't express is a bug in the format. / *Konten adalah data.*
4. Every feature ships with EN + ID text and docs. / *Setiap fitur hadir dalam dua bahasa.*
