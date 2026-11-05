#!/usr/bin/env bash
# Builds self-contained release packages. Usage: scripts/publish.sh [rid ...]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
rids=("${@:-linux-x64 win-x64 osx-x64 osx-arm64}")
for rid in ${rids[@]}; do
  dest="$root/artifacts/DjancoegTournament-$rid"
  echo "== $rid -> $dest"
  dotnet publish "$root/src/Djancoeg.Client" -c Release -r "$rid" --self-contained true -o "$dest"
  dotnet publish "$root/src/Djancoeg.Server" -c Release -r "$rid" --self-contained true -p:PublishSingleFile=true -o "$dest"
  (cd "$root/artifacts" && tar -czf "DjancoegTournament-$rid.tar.gz" "DjancoegTournament-$rid")
done
