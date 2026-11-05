#!/usr/bin/env bash
# Builds Linux packages: AppImage, .deb and a portable tar.gz.
# Usage: installer/linux/build.sh [version]   (needs: dotnet, dpkg-deb; downloads appimagetool)
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
here="$root/installer/linux"
version="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -1)}"
rid=linux-x64
pub="$root/artifacts/DjancoegTournament-$rid"
out="$root/artifacts/installers"
work="$root/artifacts/linux-work"
mkdir -p "$out"
rm -rf "$work" && mkdir -p "$work"

if [[ "${SKIP_PUBLISH:-0}" != "1" ]]; then
  rm -rf "$pub"
  dotnet publish "$root/src/Djancoeg.Client" -c Release -r $rid --self-contained true -o "$pub"
  dotnet publish "$root/src/Djancoeg.Server" -c Release -r $rid --self-contained true -p:PublishSingleFile=true -o "$pub"
fi
rm -f "$pub"/*.pdb "$pub/.itch.toml"
chmod +x "$pub/DjancoegTournament" "$pub/djancoeg-server"

# ---- portable tar.gz
tar -C "$root/artifacts" -czf "$out/DjancoegTournament-linux-x64.tar.gz" "DjancoegTournament-$rid"

# ---- .deb  (/opt/djancoeg-tournament, launchers in /usr/bin)
deb="$work/deb"
mkdir -p "$deb/DEBIAN" "$deb/opt/djancoeg-tournament" "$deb/usr/bin" "$deb/usr/share/applications" "$deb/usr/share/icons/hicolor/256x256/apps"
cp -a "$pub/." "$deb/opt/djancoeg-tournament/"
cp "$root/installer/windows/installed.marker" "$deb/opt/djancoeg-tournament/"
cat > "$deb/usr/bin/djancoeg-tournament" <<'SH'
#!/bin/sh
exec /opt/djancoeg-tournament/DjancoegTournament "$@"
SH
cat > "$deb/usr/bin/djancoeg-server" <<'SH'
#!/bin/sh
exec /opt/djancoeg-tournament/djancoeg-server "$@"
SH
chmod 755 "$deb/usr/bin/djancoeg-tournament" "$deb/usr/bin/djancoeg-server"
cp "$here/djancoeg-tournament.desktop" "$deb/usr/share/applications/"
cp "$root/assets/icon/djancoeg-icon.png" "$deb/usr/share/icons/hicolor/256x256/apps/djancoeg-tournament.png"
size=$(du -sk "$deb/opt" | cut -f1)
cat > "$deb/DEBIAN/control" <<CTRL
Package: djancoeg-tournament
Version: $version
Section: games
Priority: optional
Architecture: amd64
Installed-Size: $size
Depends: libc6, libgl1, libx11-6
Maintainer: Syubadubin Studios <noreply@github.com>
Homepage: https://github.com/SyubadubinStudios/DjancoegTournament
Description: Djancoeg Tournament - Ultimate Indonesian Chaos Battle Arena
 Multiplayer first/third-person arena shooter with Indonesian folk legends,
 professions, ghosts and memes. Campaign, level editor, LAN/online play, mods.
 Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios.
CTRL
find "$deb" -type d -exec chmod 755 {} +
dpkg-deb --root-owner-group --build "$deb" "$out/djancoeg-tournament_amd64.deb"

# ---- AppImage
app="$work/AppDir"
mkdir -p "$app/usr/lib/djancoeg-tournament"
cp -a "$pub/." "$app/usr/lib/djancoeg-tournament/"
cp "$here/djancoeg-tournament.desktop" "$app/"
cp "$root/assets/icon/djancoeg-icon.png" "$app/djancoeg-tournament.png"
cat > "$app/AppRun" <<'SH'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
if [ "$1" = "--server" ]; then shift; exec "$HERE/usr/lib/djancoeg-tournament/djancoeg-server" "$@"; fi
exec "$HERE/usr/lib/djancoeg-tournament/DjancoegTournament" "$@"
SH
chmod +x "$app/AppRun"
tool="$work/appimagetool"
curl -fsSL -o "$tool" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x "$tool"
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$tool" --no-appstream "$app" "$out/DjancoegTournament-x86_64.AppImage"

ls -la "$out"
