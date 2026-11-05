#!/usr/bin/env bash
# Builds a Flatpak bundle from the published linux-x64 folder (run installer/linux/build.sh first, or set SKIP_PUBLISH=0).
# Needs: flatpak, flatpak-builder (the Freedesktop 24.08 runtime/SDK are installed from Flathub for the user).
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
here="$root/installer/linux"
version="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -1)}"
pub="$root/artifacts/DjancoegTournament-linux-x64"
out="$root/artifacts/installers"
src="$root/artifacts/flatpak-src"
[[ -x "$pub/DjancoegTournament" ]] || { echo "Publish linux-x64 first (installer/linux/build.sh)"; exit 1; }
rm -rf "$src" && mkdir -p "$src" "$out"
cp -a "$pub" "$src/game"
cp "$root/installer/windows/installed.marker" "$src/"
cp "$root/assets/icon/djancoeg-icon.png" "$src/icon.png"
sed 's/^Icon=.*/Icon=com.syubadubin.DjancoegTournament/' "$here/djancoeg-tournament.desktop" > "$src/com.syubadubin.DjancoegTournament.desktop"
sed -e "s/__VERSION__/$version/" -e "s/__DATE__/$(date +%Y-%m-%d)/" "$here/flatpak/com.syubadubin.DjancoegTournament.metainfo.xml" > "$src/com.syubadubin.DjancoegTournament.metainfo.xml"
printf '#!/bin/sh\nexec /app/lib/djancoeg-tournament/DjancoegTournament "$@"\n' > "$src/djancoeg-tournament.sh"
printf '#!/bin/sh\nexec /app/lib/djancoeg-tournament/djancoeg-server "$@"\n' > "$src/djancoeg-server.sh"

flatpak remote-add --user --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
# Install the runtime + SDK non-interactively (flatpak-builder --install-deps-from can stop at a [Y/n] prompt).
flatpak install --user -y --noninteractive flathub org.freedesktop.Platform//24.08 org.freedesktop.Sdk//24.08
work="$root/artifacts/flatpak-build"
flatpak-builder --user --force-clean --disable-rofiles-fuse --repo="$root/artifacts/flatpak-repo" "$work" "$here/flatpak/com.syubadubin.DjancoegTournament.yml"
flatpak build-bundle "$root/artifacts/flatpak-repo" "$out/DjancoegTournament-x86_64.flatpak" com.syubadubin.DjancoegTournament \
  --runtime-repo=https://dl.flathub.org/repo/flathub.flatpakrepo
ls -la "$out"/*.flatpak
