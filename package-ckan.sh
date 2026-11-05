#!/usr/bin/env bash
# Package GameData/FarHorizons as a zip plus a matching .ckan file, so CKAN can install and track it.
#   ./package-ckan.sh                 # -> dist/FarHorizons-<version>.zip, dist/FarHorizons.ckan
# Then install with CKAN (remove any hand-copied GameData/FarHorizons first):
#   ckan install --instance <name> -c dist/FarHorizons.ckan
# Build the plugin first (./build.sh) so Plugins/FarHorizons.dll is current.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dist="$here/dist"
verfile="$here/GameData/FarHorizons/FarHorizons.version"

if [ ! -f "$here/GameData/FarHorizons/Plugins/FarHorizons.dll" ]; then
  echo "Plugins/FarHorizons.dll missing; run ./build.sh first." >&2
  exit 1
fi

num() { sed -n "s/.*\"VERSION\"[^}]*\"$1\": *\([0-9]*\).*/\1/p" "$verfile"; }
version="$(num MAJOR).$(num MINOR).$(num PATCH)"
zipname="FarHorizons-$version.zip"

mkdir -p "$dist"
rm -f "$dist/$zipname"
# Windows' bsdtar writes zips with forward-slash paths (PowerShell 5.1's Compress-Archive does not).
tar_exe=tar
[ -x /c/Windows/System32/tar.exe ] && tar_exe=/c/Windows/System32/tar.exe
(cd "$here" && "$tar_exe" -a -c -f "$dist/$zipname" --exclude .gitkeep GameData/FarHorizons LICENSE README.md)

size=$(wc -c < "$dist/$zipname" | tr -d ' ')
sha1=$(sha1sum "$dist/$zipname" | cut -d' ' -f1 | tr a-f A-F)
sha256=$(sha256sum "$dist/$zipname" | cut -d' ' -f1 | tr a-f A-F)
if command -v cygpath >/dev/null; then winpath="$(cygpath -m "$dist/$zipname")"; else winpath="$dist/$zipname"; fi
url="file:///${winpath#/}"

cat > "$dist/FarHorizons.ckan" <<EOF
{
    "spec_version": "v1.4",
    "identifier": "FarHorizons",
    "name": "Far Horizons Science",
    "abstract": "Deep-space science expansion: cores, solar wind, comet dust, in-flight analysis, real photos, entry probes, seismology and heliosphere probes.",
    "author": "Far Horizons contributors",
    "version": "$version",
    "ksp_version_min": "1.12",
    "ksp_version_max": "1.12",
    "license": "MIT",
    "depends": [ { "name": "ModuleManager" } ],
    "install": [ { "file": "GameData/FarHorizons", "install_to": "GameData" } ],
    "download": "$url",
    "download_size": $size,
    "download_hash": { "sha1": "$sha1", "sha256": "$sha256" },
    "download_content_type": "application/zip"
}
EOF
echo "Wrote $dist/$zipname and $dist/FarHorizons.ckan (version $version)"
