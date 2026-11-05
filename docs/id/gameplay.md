# Gameplay

*Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.*

## Kontrol

| Tombol | Aksi |
|---|---|
| W A S D | Jalan (gaya Quake: bunny-hop menjaga kecepatan) |
| Spasi | Lompat (tahan untuk lompat terus) |
| Ctrl / C | Jongkok |
| Klik kiri | Tembak |
| Q / klik kanan | Jurus |
| 1–7, scroll mouse | Ganti senjata |
| V | Kamera orang pertama / ketiga |
| T | Ejek (balon kata) |
| Tab | Papan skor |
| Y | Chat |
| Esc | Menu jeda (ganti jagoan, kamera, pengaturan, keluar) |

## HUD

Info vital dilukis seperti pelat nomor kendaraan: **Darah** dan **Zirah** di kiri bawah, **Peluru** di kanan bawah lengkap dengan label nama senjata dan deretan slot di atasnya. Lingkaran di tengah adalah jeda jurus; berubah kuning dengan tulisan **SIAP** saat bisa dipakai. Jam pertandingan dan baris skor ada di atas, dan kabar pembunuhan di kanan memberi tahu siapa *nyentil* siapa.

![HUD orang pertama](../screenshots/game-id-pasar-malam.png)

## Jagoan

| Jagoan | Kategori | Darah | Kecepatan | Jurus |
|---|---|---|---|---|
| Gatot Kaca | Legenda | 110 | 100% | **Otot Kawat Tulang Besi** — lompatan super |
| Si Pitung | Legenda | 100 | 105% | **Golok Kilat** — melesat |
| Malin Kundang | Legenda | 120 | 95% | **Kutukan Bunda** — kulit batu (damage tinggal 35%, 5 detik) |
| Tukang Ojol | Profesi | 100 | 110% | **Gas Pol Orderan** — ngebut (gerak ×1,5, tembak lebih cepat) |
| Emak-Emak Daster | Profesi | 115 | 100% | **Omelan Emak** — gelombang kejut |
| Satpam Komplek | Profesi | 110 | 100% | **Kopi Pos Ronda** — pulih seketika |
| Pocong | Hantu | 100 | 100% (lompat 125%) | **Lompat Pocong** — lompatan raksasa |
| Kuntilanak | Hantu | 90 | 105% | **Hihihi Menghilang** — tak terlihat sampai menembak |
| Tuyul | Hantu | 75 | 120%, mungil | **Kabur Nyolong** — ngebut |
| Genderuwo | Hantu | 150 | 85%, besar | **Raungan Rimba** — gelombang kejut besar |
| Komodo | Hewan | 130 | 95% | **Terjangan Naga** — terjang dan gigit yang tertabrak |
| Kucing Oren | Hewan | 85 | 115%, kecil | **Mode Barbar** — ngebut |
| Bocil Kematian | Meme | 80 | 112%, kecil | **Teriak Mabar** — gelombang kejut |

Ukuran berpengaruh: jagoan kecil lebih susah ditembak, jagoan besar lebih susah didorong. Contoh mod menambahkan **Abang Bakso** (jurus *Gerobak Ngebut*).

## Senjata

| Slot | Senjata | Jenis | Catatan |
|---|---|---|---|
| 1 | Raket Nyamuk Listrik | Jarak dekat | Selalu ada, damage 45, dorongan besar |
| 2 | Ketapel Kampung | Hitscan | Senjata awal, akurat, kena kepala ×1,5 |
| 3 | Shotgun Panci | Hitscan 9 peluru | Mematikan dari dekat |
| 4 | Gatling Sandal Jepit | Proyektil | Hujan sandal, bidik ke depan target |
| 5 | Petasan Banting | Proyektil memantul | Ledakan area, cocok buat pojokan |
| 6 | Bazooka Sambal | Roket | Ledakan besar; tembak kaki sambil lompat untuk **rocket jump** |
| 7 | Bambu Runcing Sakti | Hitscan menembus | Tembus semua yang berbaris |

Ledakan yang mengenai diri sendiri hanya separuh damage, tapi dorongannya penuh — itulah cara rocket jump.

## Item

| Item | Efek |
|---|---|
| Es Teh Manis | +25 darah (sampai maksimal) |
| Nasi Padang | +100 darah, bisa sampai 200 (berkurang pelan-pelan) |
| Helm SNI | +50 zirah (zirah menyerap ⅔ damage) |
| Rompi | +15 zirah |
| Kardus amunisi | Peluru untuk semua senjata yang dimiliki |
| Jamu | Damage ×3 selama 15 detik — pemiliknya bersinar merah |
| Senjata | Senjata plus peluru |

## Fisika kacau

- **Dorongan (knockback)** dari setiap serangan; ledakan melempar pemain dan properti.
- **Properti** (gerobak, galon, peti, karung, ban) bisa ditendang, ditembak, dan diledakkan. Properti yang terbang lebih dari ~9 m/s melukai yang tertabrak — poinnya untuk yang terakhir menyentuh.
- **Kasur lompat** melempar ke jalan layang, atap, dan kanopi panggung.
- **Kali dan jurang**: jatuh keluar arena berarti tewas; yang terakhir menyerangmu dalam 5 detik mendapat poin.

## Arena

| Arena | Suasana | Sorotan |
|---|---|---|
| Pasar Malam Ceria | Malam, lampu bohlam | Panggung dangdut dengan kanopi, atap tenda yang bisa dipijak, jalan layang, bianglala |
| Terminal Antar Kota | Senja | Jembatan penyeberangan, peron, atap bus lewat kasur lompat |
| Sawah & Pos Ronda | Siang terang | Sawah terbuka, terasering, pos ronda — surganya penembak jitu |
| Atap Kampung | Matahari terbenam | Atap berbeda tinggi, gang sempit, kali yang bisa bikin kecebur |
| Lapangan Tujuhbelasan (mod) | Siang | Panjat pinang dengan Jamu di puncaknya |

## Mode

| Mode | Aturan |
|---|---|
| **Tawuran Bebas** | +1 tiap membunuh, −1 tiap bunuh diri. Duluan capai batas skor, atau skor tertinggi saat waktu habis. |
| **Perang Kampung** | Kampung Atas vs Kampung Bawah. Skor dihitung per tim; tembak teman mati secara bawaan. |
| **Raja Warung** | Berdiri di zona **sendirian** untuk dapat 1 poin per detik. Dua orang di dalam = direbutkan. |

Setelah pertandingan selesai papan skor menampilkan pemenang selama 10 detik; server yang dibuka lalu pindah ke arena berikutnya.
