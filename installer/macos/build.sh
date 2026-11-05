#!/usr/bin/env bash
# Builds macOS .app bundles and drag-to-Applications .dmg images (Apple Silicon and Intel).
# Usage: installer/macos/build.sh [version]   (run on macOS: needs dotnet, sips, iconutil, hdiutil)
# Signing: ad-hoc by default (first launch needs right-click > Open). With MACOS_SIGN_IDENTITY ("Developer ID Application: ...")
# in an unlocked keychain the app is signed with the hardened runtime; with APPLE_ID, APPLE_TEAM_ID and
# APPLE_APP_PASSWORD the .dmg is also notarized and stapled.
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
  if [[ -n "${MACOS_SIGN_IDENTITY:-}" ]]; then
    # Sign every Mach-O inside first, then the bundle, with the hardened runtime.
    find "$app/Contents/MacOS" -type f \( -name "*.dylib" -o -perm -u+x \) -print0 | while IFS= read -r -d '' f; do
      codesign --force --timestamp --options runtime --entitlements "$here/entitlements.plist" --sign "$MACOS_SIGN_IDENTITY" "$f"
    done
    codesign --force --timestamp --options runtime --entitlements "$here/entitlements.plist" --sign "$MACOS_SIGN_IDENTITY" "$app"
  else
    # Ad-hoc signature so Apple Silicon will run the native code at all.
    codesign --force --deep --sign - "$app"
  fi

  stage="$work/$arch/dmg"
  mkdir -p "$stage"
  cp -a "$app" "$stage/"
  ln -s /Applications "$stage/Applications"
  dmg="$out/DjancoegTournament-macOS-$arch.dmg"
  hdiutil create -volname "Djancoeg Tournament" -srcfolder "$stage" -ov -format UDZO "$dmg"
  if [[ -n "${MACOS_SIGN_IDENTITY:-}" ]]; then codesign --force --timestamp --sign "$MACOS_SIGN_IDENTITY" "$dmg"; fi
  if [[ -n "${APPLE_ID:-}" && -n "${APPLE_TEAM_ID:-}" && -n "${APPLE_APP_PASSWORD:-}" && -n "${MACOS_SIGN_IDENTITY:-}" ]]; then
    xcrun notarytool submit "$dmg" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD" --wait
    xcrun stapler staple "$dmg"
  fi
done
ls -la "$out"
