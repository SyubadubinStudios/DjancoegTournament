#!/usr/bin/env bash
# Builds macOS .app bundles and drag-to-Applications .dmg images (Apple Silicon and Intel).
# Usage: installer/macos/build.sh [version]   (run on macOS: needs dotnet, sips, iconutil, hdiutil)
# Builds are ad-hoc signed, not notarized: first launch needs right-click > Open (see README).
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
here="$root/installer/macos"
version="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -1)}"
out="$root/artifacts/installers"
work="$root/artifacts/macos-work"
mkdir -p "$out"
rm -rf "$work" && mkdir -p "$work"

# Icon (.icns) from the 256px PNG.
iconset="$work/djancoeg.iconset"
mkdir -p "$iconset"
for s in 16 32 64 128 256 512; do
  sips -z $s $s "$root/assets/icon/djancoeg-icon.png" --out "$iconset/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2)); sips -z $d $d "$root/assets/icon/djancoeg-icon.png" --out "$iconset/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$work/djancoeg.icns"

for rid in osx-arm64 osx-x64; do
  arch=$([[ $rid == osx-arm64 ]] && echo arm64 || echo x64)
  pub="$work/publish-$rid"
  dotnet publish "$root/src/Djancoeg.Client" -c Release -r $rid --self-contained true -o "$pub"
  dotnet publish "$root/src/Djancoeg.Server" -c Release -r $rid --self-contained true -p:PublishSingleFile=true -o "$pub"
  rm -f "$pub"/*.pdb "$pub/.itch.toml"

  app="$work/$arch/Djancoeg Tournament.app"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp -a "$pub/." "$app/Contents/MacOS/"
  sed "s/__VERSION__/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"
  cp "$work/djancoeg.icns" "$app/Contents/Resources/djancoeg.icns"
  chmod +x "$app/Contents/MacOS/DjancoegTournament" "$app/Contents/MacOS/djancoeg-server"
  # Ad-hoc signature so Apple Silicon will run the native code at all.
  codesign --force --deep --sign - "$app"

  stage="$work/$arch/dmg"
  mkdir -p "$stage"
  cp -a "$app" "$stage/"
  ln -s /Applications "$stage/Applications"
  hdiutil create -volname "Djancoeg Tournament" -srcfolder "$stage" -ov -format UDZO "$out/DjancoegTournament-macOS-$arch.dmg"
done
ls -la "$out"
