# Multiplayer & server

*Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.*

Semua cara bermain memakai kode server otoritatif yang sama. Latihan menjalankan server pribadi di `127.0.0.1` di dalam game; **Buka server** menjalankannya di semua antarmuka jaringan; dedicated server menjalankannya tanpa grafis.

## Cara bermain

| Menu | Yang terjadi |
|---|---|
| Latihan lawan bot | Server di dalam game, hanya loopback, port acak. Orang lain tidak bisa gabung. |
| Buka server | Server di dalam game pada UDP `27960` (bisa diubah). Terlihat di LAN; bisa didaftarkan ke master server. Arena berganti setiap pertandingan. |
| Cari server → LAN | Menyebar paket pencarian ke port 27960–27963 dan menampilkan semua server yang menjawab. |
| Cari server → Online | Meminta daftar dari master server (Pengaturan → *Alamat master server*), lalu ping tiap server. |
| Sambung langsung | Ketik `ip:port` atau `hostname:port`. |
| Tonton | Gabung ke server terpilih sebagai penonton (tanpa slot pemain, maksimal 8 per server). |

![Pencari server](../screenshots/browser-id.png)

## Dedicated server

```bash
dotnet run --project src/Djancoeg.Server -c Release -- --name "Warung Tawuran" --arena pasar-malam --mode ffa --bots 6
# atau dari paket rilis:
./djancoeg-server --rotation pelabuhan,stasiun-kereta,kantor-kelurahan --mode ctf --score 3 --score 50 --time 900
```

| Opsi | Bawaan | Arti |
|---|---|---|
| `--port` | 27960 | Port UDP |
| `--name` | Warung Tawuran | Nama di daftar server |
| `--arena` | pasar-malam | Arena pertama (id arena bawaan atau mod) |
| `--rotation a,b,c` | — | Urutan arena setelah tiap pertandingan |
| `--mode` | ffa | `ffa`, `tdm`, `koth`, `ctf` (Rebutan Gorengan), atau `race` (Panjat Pinang) |
| `--score` | 25 | Batas skor |
| `--time` | 600 | Batas waktu dalam detik (0 = tanpa batas) |
| `--bots` | 5 | Bot pengisi slot kosong (otomatis keluar saat pemain datang) |
| `--skill` | 0.5 | Kehebatan bot 0–1 |
| `--max` | 16 | Pemain maksimal (2–16) |
| `--friendly-fire` | mati | Damage ke teman setim di `tdm` |
| `--master host:port` | — | Daftarkan ke master server tiap 20 detik |
| `--mods dir` / `--no-mods` | ./mods | Folder mod, atau hanya konten bawaan |
| `--config file.json` | — | Muat pengaturan pertandingan dari JSON |
| `--password <teks>` | — | Pemain harus memasukkan password ini untuk gabung |
| `--rcon <teks>` | — | Password admin untuk `!login` di chat |
| `--data <dir>` | ./data | Tempat `stats.json` (statistik karier) dan `bans.txt` disimpan |
| `--no-votes` | voting aktif | Matikan `!vote` |
| `--no-creeps` | makhluk aktif | Matikan Makhluk Arena |
| `--max` | 16 | Pemain maksimal, **sampai 32** |

Contoh file `--config`:

```json
{ "Mode": "KingOfTheHill", "ArenaId": "sawah-ronda", "ScoreLimit": 120, "TimeLimitSeconds": 900, "BotCount": 4, "BotSkill": 0.7, "MaxPlayers": 12, "ServerName": "Raja Pos Ronda" }
```

Tekan **Ctrl+C** untuk berhenti; pemain akan diberi tahu.

### Menjalankan sebagai service (Linux)

```ini
# /etc/systemd/system/djancoeg.service
[Unit]
Description=Djancoeg Tournament server
After=network-online.target

[Service]
WorkingDirectory=/opt/djancoeg
ExecStart=/opt/djancoeg/djancoeg-server --rotation pasar-malam,terminal-bus,sawah-ronda,atap-kampung --bots 4 --master master.example.org:27950
Restart=on-failure
User=djancoeg

[Install]
WantedBy=multi-user.target
```

## Master server (daftar server online)

```bash
./djancoeg-server master --port 27950
```

Server game yang dijalankan dengan `--master host:27950` mengirim heartbeat tiap 20 detik dan dihapus dari daftar setelah 60 detik tanpa kabar. Pemain mengisi *Alamat master server* di Pengaturan lalu membuka **Cari server → Online**.

## Port dan firewall

| Port | Protokol | Untuk |
|---|---|---|
| 27960 | UDP | Server game (pemain menyambung ke sini; pencarian LAN juga) |
| 27950 | UDP | Master server (kalau kamu menjalankannya) |

Untuk server internet, forward port game di router. Hanya server yang butuh port terbuka; klien cukup mengirim UDP keluar.

## Mod dan multiplayer

Server dan setiap klien harus memuat konten yang sama. Saat menyambung, klien mengirim **tanda tangan konten** (hash dari semua id senjata, jagoan, dan arena beserta angka pentingnya). Kalau berbeda, server menolak dan menyebutkan mod yang dipakai. Di daftar server, server seperti itu ditandai *mod beda*.

## Netcode dalam satu paragraf

Server mensimulasikan pada **60 Hz** dan mengirim snapshot ke setiap klien pada **30 Hz**. Klien mengirim input setiap tick (4 input terakhir sekaligus, jadi paket hilang tidak berpengaruh), **memprediksi** gerakannya sendiri dengan kode gerak yang persis sama, lalu **merekonsiliasi** saat snapshot datang dengan memutar ulang input yang belum dikonfirmasi. Pemain lain **diinterpolasi** 100 ms di belakang. Senjata hitscan memakai **kompensasi lag**: server memundurkan posisi pemain lain sebesar latensi penembak + jeda interpolasi. Event (tembakan, kill, item) ikut di dalam snapshot dengan konfirmasi, jadi tidak pernah hilang. Detail: [arsitektur.md](arsitektur.md#jaringan).

## Keamanan

Setiap koneksi diawali **tantangan cookie tanpa state**: server membalas `Connect` pertama dengan cookie `Challenge` kecil (HMAC dari alamat klien dan waktu, berlaku 10 detik) dan baru membuat state setelah klien mengirimnya kembali. Alamat palsu tidak pernah dapat slot, dan balasannya lebih kecil dari permintaan sehingga server tidak bisa dipakai untuk serangan amplifikasi.

Kalau server berpassword, klien membuktikan bahwa ia tahu passwordnya dengan `HMAC-SHA256(SHA256(password), cookie)`; passwordnya sendiri tidak pernah dikirim lewat jaringan.

Lalu kedua pihak melakukan pertukaran kunci **ECDH (P-256)** dan menurunkan dua kunci **AES-256-GCM** dengan HKDF (garamnya cookie). Semua setelah `Accept` — input, snapshot, chat, perintah admin — terenkripsi dan terautentikasi, dengan jendela anti-replay 64 paket. Lalu lintas pencarian server dan master server tetap terbuka (memang informasi publik).

> Pertukaran kunci tidak diautentikasi dengan sertifikat, jadi melindungi dari penyadapan dan perubahan oleh penyerang pasif, bukan dari man-in-the-middle aktif yang menguasai jalur ke server.

## Konsol admin, rcon, dan voting

Dedicated server membaca perintah dari konsolnya. Admin bisa menjalankan perintah yang sama di chat dengan awalan `!`: pemain dari komputer yang sama (listen server) otomatis admin, yang lain mengetik `!login <password rcon>`.

| Perintah | Efek |
|---|---|
| `status` | Server, arena, mode, dan pemain beserta ping |
| `kick <id\|nama> [alasan]` | Keluarkan pemain |
| `ban <id\|nama\|ip> [alasan]` / `unban <ip>` / `bans` | Daftar blokir, disimpan di `data/bans.txt` |
| `map <arena> [mode]` | Ganti arena (dan mode) sekarang |
| `mode <ffa\|tdm\|koth\|ctf\|race>` | Ganti mode sekarang |
| `bots <n>` / `skill <0-1>` | Jumlah dan kehebatan bot tanpa restart |
| `restart` | Ulang pertandingan |
| `say <teks>` | Pesan server ke semua orang |
| `password <teks\|off>` | Ganti password masuk |
| `top [n]` | Papan peringkat karier |

Pemain bisa voting: `!vote map <arena>`, `!vote mode <mode>`, `!vote restart`. Semua orang melihat spanduk kuning dan menjawab dengan **F1 (setuju) / F2 (tolak)** atau `!yes` / `!no`; lolos kalau mayoritas pemain manusia setuju dalam 30 detik.

## NAT punch-through

Server yang terdaftar di master server bisa dimasuki dari balik router rumahan. Saat kamu gabung dari daftar **Online**, game meminta master memperkenalkanmu; master memberi tahu server, lalu server mengirim beberapa paket ke alamat publikmu untuk membuka pemetaan router-nya sementara klienmu terus mengetuk. Cara ini berhasil di kebanyakan router rumahan (cone NAT); NAT simetris tetap butuh port forwarding.

## Statistik karier

Server menyimpan statistik per pemain (kill, mati, menang, jumlah main, gorengan yang dicetak, hadiah Panjat Pinang, ngamuk terbaik, waktu main) di `data/stats.json`, dengan kunci pemain acak yang dibuat tiap instalasi game — tanpa akun. Buka **Jeda → Peringkat server** di tengah pertandingan; **Papan peringkat** di menu utama menampilkan latihan dan server buatanmu di komputer ini.
