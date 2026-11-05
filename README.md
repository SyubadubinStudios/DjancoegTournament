# Djancoeg Tournament

**Ultimate Indonesian Chaos Battle Arena** — a first- and third-person multiplayer arena shooter where wayang heroes, ride-hailing drivers, pocong and orange cats settle things at the night market.

> **Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios**
> *Made by Ariana Mischa Fadhila of Syubadubin Studios*

**[⬇ Download / Unduh](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest)** · [English](#english) · [Bahasa Indonesia](#bahasa-indonesia) · Docs: [EN](docs/en/README.md) / [ID](docs/id/README.md) · [Roadmap](PLAN.md) · [Progress](Progress.md)

![Main menu](docs/screenshots/menu-en.png)

---

## English

### What it is

| | |
|---|---|
| **Campaign** | *Tur Nusantara*: 20 stages across Java, Sumatra, Borneo, Sulawesi and Nusa Tenggara, 3 stars each, 6 boss fights, illustrated story stills, **co-op over LAN** |
| **Level editor** | Build arenas in the game with drag gizmos, copy/paste between arenas, open built-in arenas as templates, pick the sky, test-play with F5, export `.djmod` |
| **Destruction** | Stalls, benches, counters and water tanks break piece by piece; per-limb ragdolls |
| **Achievements** | 12 local achievements with toasts (Steam mapping included) |
| **Modes** | Free-for-All Brawl (*Tawuran Bebas*), Village vs Village team deathmatch (*Perang Kampung*), King of the Warung (*Raja Warung*), Snack Snatch capture-the-flag (*Rebutan Gorengan*), Greased Pole Race (*Panjat Pinang*) |
| **Views** | First person and third person — press **V** at any time |
| **Multiplayer** | Practice vs bots, LAN (auto-discovery), online via a master server with NAT punch-through, direct IP, dedicated server, up to 32 players |
| **Online services** | Encrypted connections (ECDH + AES-GCM), server passwords, admin console & rcon, kick/ban, map/mode votes, career stats & leaderboards, online Workshop |
| **Cross platform** | Windows, Linux, macOS (x64 / Apple Silicon) — installers for each, .NET 10 + raylib |
| **Fighters** | 17 built-in: legends, professions, ghosts, animals and memes, each with a signature ability |
| **Arenas** | Night Market, Bus Terminal, Rice Fields & Watch Post, Kampung Rooftops, Harbour, Old Train Station, Village Office |
| **Weapons** | 10, from a mosquito racket to a satay fan that blows rockets back at their owner |
| **Chaos** | Knockback and rocket jumps, kickable carts and water jugs that hurt, jump pads |
| **Arena Creeps** | 8 absurd monsters crash matches at random — wedding zombies, a giant floating head, Buto Ijo, a burrowing giant worm and more (toggle in Settings) |
| **Effects** | Blood splats or confetti (your choice), bullet holes, smoke, layered explosions with shockwaves and debris |
| **Modding** | Data-driven fighters, weapons and arenas in JSON; `.djmod` packages; in-game Workshop |
| **Controls** | Rebindable keys, full gamepad support, per-weapon crosshairs, surface footsteps |
| **Spectating** | Kill cam, spectator follow and free cameras (*Watch* in the browser) |
| **Accessibility** | Colour-blind team colours, subtitles, reduced shake/flashes, render scale for slow GPUs |
| **Languages** | English and Bahasa Indonesia, switchable in Settings |

| First person | Third person |
|---|---|
| ![First person](docs/screenshots/game-en-terminal-bus.png) | ![Third person](docs/screenshots/game-tps-en-lapangan-17an.png) |

| Tur Nusantara | Level editor |
|---|---|
| ![Campaign map](docs/screenshots/campaign-en.png) | ![Level editor](docs/screenshots/editor-en.png) |
| ![Story still](docs/screenshots/cutscene-id.png) | ![Ragdolls](docs/screenshots/ragdoll-id.png) |

![Fighter select](docs/screenshots/fighters-en.png)

![Practice setup with rendered arena previews](docs/screenshots/setup-en.png)

### Download

Get the latest release from **[GitHub Releases](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest)**. Every package is self-contained: no .NET install is needed.

| Platform | Installer | Portable |
|---|---|---|
| **Windows 10/11 (x64)** | [DjancoegTournament-Setup-win-x64.exe](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/DjancoegTournament-Setup-win-x64.exe) | [zip](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/DjancoegTournament-win-x64-portable.zip) |
| **Linux (x64)** | [AppImage](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/DjancoegTournament-x86_64.AppImage) · [.deb (Debian/Ubuntu)](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/djancoeg-tournament_amd64.deb) | [tar.gz](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/DjancoegTournament-linux-x64.tar.gz) |
| **macOS 12+ Apple Silicon** | [DjancoegTournament-macOS-arm64.dmg](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/DjancoegTournament-macOS-arm64.dmg) | — |
| **macOS 12+ Intel** | [DjancoegTournament-macOS-x64.dmg](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/DjancoegTournament-macOS-x64.dmg) | — |

- **Windows:** run the setup. It installs per-user (no admin needed), adds Start-menu shortcuts for the game, the dedicated server and the mods folder, and can open the firewall for LAN/online play. SmartScreen may warn because the installer isn't code-signed: choose *More info → Run anyway*.
- **Linux AppImage:** `chmod +x DjancoegTournament-x86_64.AppImage && ./DjancoegTournament-x86_64.AppImage` (add `--server` to start the dedicated server).
- **Linux .deb:** `sudo apt install ./djancoeg-tournament_amd64.deb`, then start *Djancoeg Tournament* from the app menu, or run `djancoeg-tournament` / `djancoeg-server`.
- **macOS:** open the .dmg and drag the game to *Applications*. The build is not notarized, so the first time, right-click the app → *Open*. If macOS says the app is damaged, run `xattr -dr com.apple.quarantine "/Applications/Djancoeg Tournament.app"`.
- **Saves:** settings, campaign progress, achievements, mods and your editor arenas live in `%APPDATA%\DjancoegTournament` on Windows and `~/.config/DjancoegTournament` on Linux/macOS, so updating or uninstalling keeps them. Portable builds keep mods next to the game.
- **Checking downloads:** `SHA256SUMS.txt` in each release lets you verify them.

### Build from source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) and a GPU with OpenGL 3.3.

```bash
dotnet build                                   # build everything
dotnet test                                    # run the test suite
dotnet run --project src/Djancoeg.Client       # play
dotnet run --project src/Djancoeg.Server -- --bots 6 --arena pasar-malam   # dedicated server
```

Then choose **Tur Nusantara** for the campaign, or **Practice vs bots**, pick your fighter and press **Start match**.

**Controls (rebindable, gamepad supported):** WASD move · Space jump · Ctrl crouch · Left mouse fire · Q / right mouse ability · 1-7, E or wheel weapons · V first/third person · T taunt · Tab scores · Y chat · Esc menu.

### Documentation

| Guide | |
|---|---|
| [Getting started](docs/en/getting-started.md) | Install, build, run, command-line options |
| [Gameplay](docs/en/gameplay.md) | Fighters, weapons, pickups, arenas, modes |
| [Multiplayer & servers](docs/en/multiplayer.md) | Hosting, LAN, online master server, dedicated server, ports |
| [Modding](docs/en/modding.md) | Make fighters, weapons and arenas; package and share `.djmod` |
| [Campaign, editor & achievements](docs/en/campaign-and-editor.md) | Tur Nusantara, the level editor, destruction, achievements, distribution |
| [Architecture](docs/en/architecture.md) | Projects, simulation, netcode, performance and memory |
| [Art pipeline](docs/en/assets.md) | Blender sculpt/rig/animate, Rodin 3D & 2D art, screenshots |

### Project layout

```
src/Djancoeg.Core      Simulation: movement, combat, bots, rules, content (no rendering, no allocation per tick)
src/Djancoeg.Net       UDP protocol, server, client prediction/interpolation, LAN discovery, master server
src/Djancoeg.Modding   Mod loading, validation, packaging (.djmod), level-editor model
src/Djancoeg.Server    Dedicated server, master server and mod tools (djancoeg-server)
src/Djancoeg.Client    The game (raylib): renderer, UI, audio, HUD
tests/Djancoeg.Tests   xUnit tests: physics, combat, arenas, bots, protocol, loopback networking, mods
mods/warung-bakso      Example mod: a meatball seller, a bowl launcher and the Independence Day field
assets/                Fonts, Rodin art, Blender-built fighters, props, voice lines
tools/blender          Reproducible character sculpt + rig + animation script
installer/             Windows (Inno Setup), Linux (AppImage, .deb) and macOS (.app, .dmg) installer builds
distribution/          itch.io (butler) and Steam (ContentBuilder) templates, achievement mapping
.github/workflows      CI (build + test) and Release (installers for every platform on tag v*)
```

---

## Bahasa Indonesia

### Apa ini

| | |
|---|---|
| **Kampanye** | *Tur Nusantara*: 20 tahap di Jawa, Sumatera, Kalimantan, Sulawesi, dan Nusa Tenggara, 3 bintang per tahap, 6 pertarungan bos, gambar cerita, **main bareng lewat LAN** |
| **Editor arena** | Bikin arena dengan gizmo seret, salin/tempel antar arena, buka arena bawaan sebagai templat, pilih suasana langit, uji main dengan F5, ekspor `.djmod` |
| **Kehancuran** | Lapak, bangku, loket, dan toren hancur sepotong demi sepotong; ragdoll per anggota tubuh |
| **Pencapaian** | 12 pencapaian lokal dengan notifikasi (sudah ada pemetaan Steam) |
| **Mode** | Tawuran Bebas, Perang Kampung (tim), Raja Warung (kuasai zona), Rebutan Gorengan (capture the flag), Panjat Pinang (balapan ke puncak) |
| **Kamera** | Orang pertama dan orang ketiga — tekan **V** kapan saja |
| **Multiplayer** | Latihan lawan bot, LAN (terdeteksi otomatis), online lewat master server dengan NAT punch-through, IP langsung, dedicated server, sampai 32 pemain |
| **Layanan online** | Koneksi terenkripsi (ECDH + AES-GCM), password server, konsol admin & rcon, kick/ban, voting arena/mode, statistik karier & papan peringkat, Workshop online |
| **Lintas platform** | Windows, Linux, macOS (x64 / Apple Silicon) — installer untuk masing-masing, .NET 10 + raylib |
| **Jagoan** | 17 bawaan: legenda, profesi, hantu, hewan, dan meme, masing-masing punya jurus |
| **Arena** | Pasar Malam Ceria, Terminal Antar Kota, Sawah & Pos Ronda, Atap Kampung, Pelabuhan, Stasiun Kereta Tua, Kantor Kelurahan |
| **Senjata** | 10, dari raket nyamuk sampai kipas sate yang memantulkan roket ke pemiliknya |
| **Kekacauan** | Knockback dan rocket jump, gerobak dan galon yang bisa ditendang (dan menyakitkan), kasur lompat |
| **Makhluk Arena** | 8 monster absurd menyerbu secara acak — zombie kondangan, kepala raksasa melayang, Buto Ijo, cacing raksasa dan lainnya (bisa dimatikan) |
| **Efek** | Darah atau konfeti (pilih sendiri), lubang peluru, asap, ledakan berlapis dengan gelombang kejut dan puing |
| **Modding** | Jagoan, senjata, dan arena berbasis JSON; paket `.djmod`; Workshop di dalam game |
| **Kontrol** | Tombol bisa diatur ulang, dukungan gamepad penuh, crosshair per senjata, suara langkah sesuai permukaan |
| **Menonton** | Kill cam, kamera penonton ikuti dan bebas (*Tonton* di pencari server) |
| **Aksesibilitas** | Warna tim ramah buta warna, subtitle, kurangi guncangan/kilatan, skala render untuk GPU lambat |
| **Bahasa** | Inggris dan Bahasa Indonesia, bisa diganti di Pengaturan |

![Menu utama](docs/screenshots/menu-id.png)

| Orang pertama | Orang ketiga |
|---|---|
| ![Orang pertama](docs/screenshots/game-id-pasar-malam.png) | ![Orang ketiga](docs/screenshots/game-tps-id-atap-kampung.png) |
| ![Rebutan Gorengan](docs/screenshots/game-tps-id-pelabuhan-ctf.png) | ![Panjat Pinang](docs/screenshots/game-tps-en-stasiun-kereta-race.png) |
| ![Tur Nusantara](docs/screenshots/campaign-id.png) | ![Pertarungan bos](docs/screenshots/boss-id.png) |

### Unduh

Ambil rilis terbaru di **[GitHub Releases](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest)**. Semua paket mandiri: tidak perlu memasang .NET.

| Platform | Installer | Portabel |
|---|---|---|
| **Windows 10/11 (x64)** | [DjancoegTournament-Setup-win-x64.exe](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/DjancoegTournament-Setup-win-x64.exe) | [zip](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/DjancoegTournament-win-x64-portable.zip) |
| **Linux (x64)** | [AppImage](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/DjancoegTournament-x86_64.AppImage) · [.deb (Debian/Ubuntu)](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/djancoeg-tournament_amd64.deb) | [tar.gz](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/DjancoegTournament-linux-x64.tar.gz) |
| **macOS 12+ Apple Silicon** | [DjancoegTournament-macOS-arm64.dmg](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/DjancoegTournament-macOS-arm64.dmg) | — |
| **macOS 12+ Intel** | [DjancoegTournament-macOS-x64.dmg](https://github.com/SyubadubinStudios/DjancoegTournament/releases/latest/download/DjancoegTournament-macOS-x64.dmg) | — |

- **Windows:** jalankan setup-nya. Pemasangan per pengguna (tanpa admin), membuat pintasan Start menu untuk game, dedicated server, dan folder mod, dan bisa membuka firewall untuk main LAN/online. SmartScreen mungkin memberi peringatan karena installer belum ditandatangani: pilih *More info → Run anyway*.
- **Linux AppImage:** `chmod +x DjancoegTournament-x86_64.AppImage && ./DjancoegTournament-x86_64.AppImage` (tambahkan `--server` untuk menjalankan dedicated server).
- **Linux .deb:** `sudo apt install ./djancoeg-tournament_amd64.deb`, lalu buka *Djancoeg Tournament* dari menu aplikasi, atau jalankan `djancoeg-tournament` / `djancoeg-server`.
- **macOS:** buka .dmg lalu seret game ke *Applications*. Build ini belum dinotarisasi, jadi saat pertama kali klik kanan aplikasinya → *Open*. Kalau macOS bilang aplikasinya rusak, jalankan `xattr -dr com.apple.quarantine "/Applications/Djancoeg Tournament.app"`.
- **Simpanan:** pengaturan, progres kampanye, pencapaian, mod, dan arena buatanmu disimpan di `%APPDATA%\DjancoegTournament` di Windows dan `~/.config/DjancoegTournament` di Linux/macOS, jadi tetap aman saat memperbarui atau menghapus game. Versi portabel menyimpan mod di sebelah game.
- **Memeriksa unduhan:** setiap rilis menyertakan `SHA256SUMS.txt` untuk verifikasi.

### Build dari kode sumber

Butuh [.NET 10 SDK](https://dotnet.microsoft.com/download) dan GPU dengan OpenGL 3.3.

```bash
dotnet build                                   # build semua proyek
dotnet test                                    # jalankan semua tes
dotnet run --project src/Djancoeg.Client       # main
dotnet run --project src/Djancoeg.Server -- --bots 6 --arena pasar-malam   # dedicated server
```

Pilih **Tur Nusantara** untuk kampanye, atau **Latihan lawan bot**, pilih jagoanmu, lalu tekan **Gas, mulai!**

**Kontrol (bisa diatur ulang, mendukung gamepad):** WASD jalan · Spasi lompat · Ctrl jongkok · Klik kiri tembak · Q / klik kanan jurus · 1-7, E, atau scroll ganti senjata · V kamera 1/3 · T ejek · Tab skor · Y chat · Esc menu.

### Dokumentasi

| Panduan | |
|---|---|
| [Memulai](docs/id/memulai.md) | Instalasi, build, menjalankan, opsi baris perintah |
| [Gameplay](docs/id/gameplay.md) | Jagoan, senjata, item, arena, mode |
| [Multiplayer & server](docs/id/multiplayer.md) | Buka server, LAN, master server online, dedicated server, port |
| [Modding](docs/id/modding.md) | Bikin jagoan, senjata, dan arena; paketkan dan bagikan `.djmod` |
| [Kampanye, editor & pencapaian](docs/id/kampanye-dan-editor.md) | Tur Nusantara, editor arena, kehancuran, pencapaian, distribusi |
| [Arsitektur](docs/id/arsitektur.md) | Proyek, simulasi, netcode, performa, dan memori |
| [Pipeline aset](docs/id/aset.md) | Sculpt/rig/animasi Blender, aset 3D & 2D Rodin, screenshot |

---

## Credits / Kredit

**Ariana Mischa Fadhila — Syubadubin Studios.**
Built with .NET 10 and [raylib](https://www.raylib.com/) via Raylib-cs. Fighters sculpted, rigged and animated in Blender (via Blender MCP); 2D art, props and voice lines generated with Rodin. Fonts: Bungee, Plus Jakarta Sans, Space Mono (SIL Open Font License, see `assets/fonts`).

Djancoeg Tournament is a comedy game. Characters are folk legends, generic professions, ghosts from Indonesian folklore, animals and memes — no real people or brands.
