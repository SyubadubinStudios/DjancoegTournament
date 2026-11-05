#!/usr/bin/env bash
# Writes winget and Homebrew manifests for a release into <dist>/manifests (zipped as package-manifests.zip).
# Usage: installer/make-manifests.sh <version> <dist-dir-with-installers>
# Submitting them is a manual step: winget -> PR to microsoft/winget-pkgs, Homebrew -> your own tap.
set -euo pipefail
version="$1"; dist="$2"
base="https://github.com/SyubadubinStudios/DjancoegTournament/releases/download/v$version"
sha() { sha256sum "$dist/$1" | cut -d' ' -f1; }
m="$dist/manifests"
w="$m/winget/manifests/s/SyubadubinStudios/DjancoegTournament/$version"
mkdir -p "$w" "$m/homebrew/Casks"
id=SyubadubinStudios.DjancoegTournament

cat > "$w/$id.yaml" <<EOF
PackageIdentifier: $id
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
EOF
cat > "$w/$id.installer.yaml" <<EOF
PackageIdentifier: $id
PackageVersion: $version
InstallerType: inno
Scope: user
InstallModes: [interactive, silent, silentWithProgress]
UpgradeBehavior: install
Installers:
  - Architecture: x64
    InstallerUrl: $base/DjancoegTournament-Setup-win-x64.exe
    InstallerSha256: $(sha DjancoegTournament-Setup-win-x64.exe | tr a-f A-F)
ManifestType: installer
ManifestVersion: 1.6.0
EOF
cat > "$w/$id.locale.en-US.yaml" <<EOF
PackageIdentifier: $id
PackageVersion: $version
PackageLocale: en-US
Publisher: Syubadubin Studios
PublisherUrl: https://github.com/SyubadubinStudios
PackageName: Djancoeg Tournament
PackageUrl: https://github.com/SyubadubinStudios/DjancoegTournament
License: Proprietary
ShortDescription: Ultimate Indonesian Chaos Battle Arena - multiplayer FPS/TPS arena shooter.
Description: Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios. Island campaign, level editor, LAN/online play and mods.
Tags: [game, fps, arena, multiplayer, indonesia]
ReleaseNotesUrl: https://github.com/SyubadubinStudios/DjancoegTournament/releases/tag/v$version
ManifestType: defaultLocale
ManifestVersion: 1.6.0
EOF

cat > "$m/homebrew/Casks/djancoeg-tournament.rb" <<EOF
cask "djancoeg-tournament" do
  arch arm: "arm64", intel: "x64"

  version "$version"
  sha256 arm:   "$(sha DjancoegTournament-macOS-arm64.dmg)",
         intel: "$(sha DjancoegTournament-macOS-x64.dmg)"

  url "https://github.com/SyubadubinStudios/DjancoegTournament/releases/download/v#{version}/DjancoegTournament-macOS-#{arch}.dmg"
  name "Djancoeg Tournament"
  desc "Ultimate Indonesian Chaos Battle Arena"
  homepage "https://github.com/SyubadubinStudios/DjancoegTournament"

  depends_on macos: ">= :monterey"

  app "Djancoeg Tournament.app"

  zap trash: "~/.config/DjancoegTournament"
end
EOF
(cd "$m" && zip -qr "$dist/package-manifests.zip" .)
rm -rf "$m"
echo "Wrote $dist/package-manifests.zip"
