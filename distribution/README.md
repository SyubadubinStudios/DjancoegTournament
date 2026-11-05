# Distribution / Distribusi

**EN** — Everything needed to ship builds outside GitHub.

| Path | What it is |
|------|------------|
| `../.github/workflows/ci.yml` | Builds and tests on Windows + Linux for every push/PR. Tags `v*` also publish self-contained zips for win-x64, linux-x64 and osx-arm64, attach them to a GitHub Release, and push to itch.io when the `BUTLER_API_KEY` secret is set (`ITCH_TARGET` repo variable, e.g. `syubadubin/djancoeg-tournament`). |
| `itch/.itch.toml` | itch app launch manifest (Play, Dedicated server, Mods folder). `scripts/publish.ps1` fills in the executable names per platform. |
| `itch/push.ps1` | `pwsh distribution/itch/push.ps1 -Channel windows -Folder artifacts/DjancoegTournament-win-x64 -Version v2.0.0` — downloads butler if missing. |
| `steam/app_build.vdf`, `steam/depot_build_*.vdf` | Steamworks ContentBuilder templates. Replace `STEAM_APP_ID` / `STEAM_DEPOT_*`, run `scripts/publish.ps1`, then `steamcmd +login <builder> +run_app_build <path>/app_build.vdf +quit`. |
| `steam/achievements.json` | Local achievement id → Steam API name / stat mapping. |

Player data (settings, campaign, achievements, stats) is stored in the OS app-data folder (`%APPDATA%/DjancoegTournament`, `~/.config/DjancoegTournament`), so depots never overwrite saves.

**ID** — Semua yang dibutuhkan untuk merilis build di luar GitHub. CI membangun dan menguji setiap push/PR; tag `v*` membuat paket zip mandiri (Windows, Linux, macOS ARM), melampirkannya ke GitHub Release, dan mengunggah ke itch.io bila secret `BUTLER_API_KEY` diisi. Templat Steam (`steam/*.vdf`) tinggal diganti ID aplikasi/depot-nya. `steam/achievements.json` memetakan id pencapaian lokal ke nama API Steam. Data pemain disimpan di folder app-data OS sehingga pembaruan tidak menimpa simpanan.

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.
