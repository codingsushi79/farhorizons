#!/usr/bin/env bash
# Build the plugin and optionally install into KSP.
#   ./build.sh "/path/to/Kerbal Space Program"           # build only
#   ./build.sh "/path/to/Kerbal Space Program" install   # build + copy GameData/FarHorizons into KSP
set -euo pipefail
KSPDIR="${1:-${KSPDIR:-}}"
if [ -z "$KSPDIR" ]; then
  echo "usage: $0 /path/to/KSP [install]" >&2
  exit 1
fi
here="$(cd "$(dirname "$0")" && pwd)"
dotnet build "$here/src/FarHorizons/FarHorizons.csproj" -c Release -p:KSPDIR="$KSPDIR"
if [ "${2:-}" = "install" ]; then
  rm -rf "$KSPDIR/GameData/FarHorizons"
  cp -R "$here/GameData/FarHorizons" "$KSPDIR/GameData/"
  echo "Installed to $KSPDIR/GameData/FarHorizons"
  if ! ls "$KSPDIR/GameData" | grep -qi '^ModuleManager.*\.dll$'; then
    echo "WARNING: ModuleManager not found in GameData. Drill/lab patches need it." >&2
  fi
fi
