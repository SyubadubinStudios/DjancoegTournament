# Arsitektur

*Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.*

## Proyek

```
Djancoeg.Core      ← tanpa dependensi. Definisi konten, tabrakan, gerak, GameWorld, bot.
   ▲   ▲
   │   Djancoeg.Modding   ← muat/validasi mod.json, pack/install .djmod (System.Text.Json source generation)
   │   ▲
Djancoeg.Net       ← transport UDP, protokol, GameServer, GameClient, ServerBrowser, MasterServer
   ▲          ▲
Djancoeg.Server   Djancoeg.Client (raylib)
(exe tanpa grafis) (renderer, UI, audio)
```

`Core`, `Modding`, dan `Net` ditandai `IsAotCompatible` / bisa di-trim; tidak ada serialisasi berbasis refleksi.

## Simulasi (`Djancoeg.Core`)

- **`GameWorld`** adalah pertandingan otoritatif: `Step()` tetap **60 Hz** di atas array datar struct `Player`, `Projectile`, `Prop`, dan `PickupState` (16 pemain, 256 proyektil, 64 properti, 64 item). Dipecah menjadi file partial: `GameWorld.cs` (pemain, spawn, tick), `GameWorld.Combat.cs` (senjata, damage, ledakan, jurus), dan `GameWorld.World.cs` (properti, item, aturan pertandingan).
- **`PlayerMovement`** bergaya Quake (gesekan tanah, akselerasi darat/udara, ramah bunny-hop) dengan tabrakan swept-AABB, sub-step agar tidak menembus dinding, naik undakan, dan menempel ke tepian. Fungsi statis murni dari `(MoveState, PlayerInput, MoveParams, CollisionWorld)`, jadi server dan prediksi klien menjalankan kode yang identik.
- **`CollisionWorld`** menyimpan kotak padat arena. Query tumpang-tindih dan sweep memakai grid XZ 6 m dengan de-duplikasi berbasis stempel (tanpa alokasi); raycast memindai array kotak datar, lebih cepat dari pohon untuk beberapa ratus kotak.
- **`BotBrain`** menghasilkan `PlayerInput` persis seperti klien manusia: memilih target yang terlihat, memilih senjata sesuai jarak, membidik ke depan proyektil, strafing, menghindari halangan, menjaga diri dari jurang, mencari item, dan pulih saat tersangkut. Kehebatan memengaruhi waktu reaksi, kecepatan putar, dan meleset bidikan.
- **`EventBuffer`** adalah ring 1024 `GameEvent` dengan id menaik (tembakan, kena, kill, ledakan, item, ejekan…). Menggerakkan efek, suara, kabar pembunuhan, dan replikasi jaringan.
- **Konten** (`WeaponDef`, `CharacterDef`, `ArenaDef`) berupa data biasa. Arena bawaan dibuat oleh `ArenaBuilder` (tangga, kasur lompat yang dihitung agar mendarat di titik tertentu). `ContentRegistry` menggabungkan konten bawaan dan mod lalu menghitung **tanda tangan konten**.

Aturan penting bagi kontributor:

- `GameWorld.Step()` tidak boleh mengalokasi memori. `BotMatchTests.Simulation_tick_does_not_allocate` mengukur 1200 tick dengan 12 bot.
- Semua damage lewat `GameWorld.ApplyDamage` (tim, Jamu ×3, kulit batu, zirah, dorongan, poin kill).
- **Indeks** senjata/jagoan/arena dikirim lewat jaringan, jadi urutan konten harus sama di server dan klien.

## Jaringan

Satu socket UDP per endpoint (`UdpTransport`) memakai overload `SendTo`/`ReceiveFrom` berbasis `SocketAddress` yang bebas alokasi. Setiap paket diawali `"DJ"`, versi protokol, dan byte tipe; pembaca berupa `ref struct` yang memeriksa batas dan menandai paket terpotong alih-alih melempar exception.

| Paket | Arah | Isi |
|---|---|---|
| Connect / Accept / Reject | K→S / S→K | nama, jagoan, tanda tangan konten / slot, arena, mode, batas |
| Input | K→S, 60 Hz | waktu gema, konfirmasi event, RTT, 4 input terakhir (masing-masing 16 byte) |
| Snapshot | S→K, 30 Hz | tick, konfirmasi input, status pertandingan, semua pemain, inventaris sendiri, bitmask item, event, proyektil, properti |
| PlayerInfo | S→K, 1 Hz + saat berubah | nama dan penanda bot |
| Chat, ChangeCharacter, Disconnect | dua arah | |
| DiscoveryRequest/Response | browser↔server | broadcast LAN / ping langsung |
| MasterHeartbeat / Query / List | server/browser↔master | daftar online |

Snapshot dianggarkan **1200 byte** (di bawah MTU umum): pemain selalu muat; lalu event (yang paling lama belum dikonfirmasi dulu), proyektil (terdekat dulu), properti yang bergerak, dan potongan bergilir properti yang diam. Posisi dikuantisasi 1/64 m, sudut 16 bit.

**Prediksi & rekonsiliasi klien** — `GameClient.SendInput` menyimpan input di ring 256 entri dan menjalankan `PlayerMovement` secara lokal. Saat snapshot datang, klien kembali ke status server pada input yang dikonfirmasi lalu memutar ulang sisanya; selisih visual dihaluskan dalam ~100 ms.

**Interpolasi** — pemain lain digambar 100 ms di belakang snapshot terbaru, dicampur di antara dua snapshot di sekitar waktu itu. Proyektil diekstrapolasi maju dari snapshot terbaru.

**Kompensasi lag** — server menyimpan riwayat posisi 32 tick per pemain. Tembakan hitscan dihitung terhadap posisi yang dimundurkan sebesar setengah RTT penembak + jeda interpolasi.

**Antrean input server** — input diterapkan satu per tick sesuai urutan; tumpukan lebih dari 4 dipangkas agar latensi tidak menumpuk.

## Klien (`Djancoeg.Client`)

- **`App`** memegang jendela, `WorldShader`, `ModelLibrary`, `SoundBank`, toolkit `Gui`, dan `Screen` aktif. Layar: menu utama, pengaturan latihan, jagoan, pencari server, workshop, pengaturan, kredit, bermain.
- **`Session`** membungkus `GameClient` dan, untuk latihan/buka server, `GameServer` di thread latar. Gerakan mouse diperbarui tiap frame; input diambil tepat 60 Hz dengan tombol yang di-latch.
- **`GameView`** mengubah snapshot + event menjadi dunia: satu mesh terpanggang per potongan arena (warna vertex + jenis permukaan di texcoord), jagoan glTF ber-skin dengan klip idle/run/shoot/die, model properti Rodin yang disesuaikan dengan kotak fisiknya, item/senjata/proyektil prosedural, partikel dan tracer yang di-pool, cahaya dinamis dari ledakan, viewmodel tanpa depth test, dan HUD.
- **`WorldShader`** — satu shader GLSL 330: matahari + ambient hemisfer + hingga 16 lampu titik, kabut eksponensial, dan pola permukaan prosedural, sehingga seluruh arena cukup satu draw call per potongan.
- **`SoundBank`** mensintesis semua efek saat start (tanpa file audio), memutarnya lewat pool alias dengan pelemahan jarak dan pan stereo, serta memuat suara penyiar dari `assets/audio/voice`.
- **UI** — `Gui` immediate-mode dengan fokus keyboard dan mouse; identitas visual ada di `Ui/Theme.cs`.

## Performa dan memori

| Praktik | Di mana |
|---|---|
| Array struct, amunisi `InlineArray`, tanpa alokasi per tick | `GameWorld`, `Entities.cs` |
| Socket bebas alokasi, `stackalloc`, serializer `ref struct` | `UdpTransport`, `NetBuffer` |
| Snapshot, efek, dan alias suara di-pool | `GameClient`, `Effects`, `SoundBank` |
| Geometri statis dipanggang sekali; satu shader | `ArenaRenderer`, `WorldShader` |
| Aset diperkecil di Blender: properti 9,6 MB → 0,15 MB (3 ribu segitiga, 512 px), jagoan ~140 KB | `assets/models` |
| JSON source-generated (ramah trim/AOT) | `ModJsonContext`, `ClientJsonContext` |
| Tiered PGO, invariant globalization, GC konkuren | `Directory.Build.props` |

Di laptop 1366×768 dengan grafis integrated, build Release berjalan ~200 FPS dengan 8 jagoan (VSync mati).

## Tes

`tests/Djancoeg.Tests` (xUnit): geometri dan penghitung kasur lompat, gerak (mendarat, dinding, tangga, tinggi lompat, kasur lompat, jongkok), pertarungan (hitscan, zirah, tim, rocket jump, skor, Raja Warung, kecebur kali, item, semua jurus), kewajaran semua arena bawaan (spawn mendarat, kasur lompat mengantar ke lantai, senjata di item ada), pertandingan bot 2 menit per arena, tick tanpa alokasi, protokol bolak-balik, klien/server loopback (sambung, prediksi, event, konten beda), ping LAN, master server, serta muat/validasi/pack/keamanan path mod.
