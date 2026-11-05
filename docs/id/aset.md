# Pipeline aset

*Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.*

| Aset | Alat | Lokasi |
|---|---|---|
| Key art, 14 potret jagoan, ikon aplikasi | Rodin (pembuat gambar), gaya lukisan bak truk | `assets/art`, `assets/icon` |
| Properti gerobak, galon, peti | Rodin (teks-ke-3D), lalu dirapikan di Blender | `assets/models/source` (mentah), `assets/models/props` (siap game) |
| 14 jagoan ber-rig dan beranimasi | Blender lewat Blender MCP (`tools/blender/build_characters.py`) | `assets/models/characters` |
| Suara penyiar | Rodin (ElevenLabs v3, Bahasa Indonesia) | `assets/audio/voice` |
| Efek suara | Disintesis di kode saat start | `src/Djancoeg.Client/Audio/SoundBank.cs` |
| Font | Bungee, Plus Jakarta Sans, Space Mono (OFL) | `assets/fonts` |

## Jagoan (Blender)

`tools/blender/build_characters.py` men-sculpt setiap jagoan sebagai "figur mainan" low-poly: primitif berwarna vertex yang terikat kaku ke rig 12 tulang bersama (pinggul, tulang belakang, dada, kepala, lengan atas/bawah, paha, betis), lalu membuat empat klip sebagai track NLA dan mengekspor glTF:

| Klip | Frame | Catatan |
|---|---|---|
| `idle` | 49 | Bernapas, senjata di depan |
| `run` | 21 | Ayunan kaki dan naik-turun (pocong melompat, kuntilanak melayang) |
| `shoot` | 9 | Hentakan |
| `die` | 24 | Jatuh ke belakang (tidak diulang) |

Jalankan dari Blender MCP (`execute_blender_code`) atau tanpa GUI:

```bash
blender -b --python tools/blender/build_characters.py -- all
blender -b --python tools/blender/build_characters.py -- pocong kuntilanak
```

Konvensi: Z ke atas di Blender, karakter menghadap −Y (glTF +Z = arah depan game), kaki di titik asal, tinggi 1,8 m. Warna disimpan sebagai sRGB mentah di atribut `FLOAT_COLOR` agar tampil persis seperti yang dibuat. Setiap keyframe menyimpan pose lengkap sehingga klip tidak saling mewarisi channel.

Untuk menambah jagoan, tambahkan entri di `CHARACTERS` (bentuk + tiga warna) dan, kalau mau, aksesori di `sculpt()`. Game otomatis menemukan `assets/models/characters/<id>.glb`.

## Properti (Rodin → Blender)

Properti mentah dari Rodin berukuran 7–10 MB dengan tekstur PBR 2K. Di Blender properti itu di-decimate menjadi 1,2–3 ribu face, ditengahkan, dan tekstur diffuse-nya dikecilkan ke 512 px (normal/metal map dibuang, karena shader game hanya memakai warna dasar). Game menyesuaikan setiap model ke kotak fisiknya dan memutarnya kalau sumbu panjangnya berbeda.

## Screenshot

Klien bisa merender layar mana saja dan menyimpannya:

```bash
DjancoegTournament --shot menu --lang id --out docs/screenshots/menu-id.png
DjancoegTournament --shot game-tps --arena atap-kampung --out shot.png
```

Scene: `menu`, `setup`, `host`, `fighters`, `browser`, `workshop`, `settings`, `credits`, `game` (orang pertama), `game-tps`, `scoreboard`. Scene game memulai latihan dengan 7 bot dan autopilot, lalu menunggu sampai pemain hidup dan sedang menghadapi musuh.

Membuat ulang semua screenshot dokumentasi (Windows, butuh sesi desktop):

```powershell
pwsh scripts/screenshots.ps1
```
