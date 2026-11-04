#!/usr/bin/env bash
# Build the plugin and a Yak package (Mac or Linux; on Windows use the GitHub Action or Git Bash).
# Usage:  ./yak/package.sh            -> dist/<package>.yak
#         ./yak/package.sh --push     -> also publishes to the Rhino package server (run `yak login` first)
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet build plugin/EnzymeIDPasses.csproj -c Release

STAGE=dist/stage
rm -rf dist && mkdir -p "$STAGE/net48" "$STAGE/net7.0"
cp plugin/bin/Release/net48/EnzymeIDPasses.rhp  "$STAGE/net48/"
cp plugin/bin/Release/net7.0/EnzymeIDPasses.rhp "$STAGE/net7.0/"
cp yak/manifest.yml assets/icon.png LICENSE "$STAGE/"

YAK="${YAK:-}"
if [ -z "$YAK" ]; then
  for c in "/Applications/Rhino 8.app/Contents/Resources/bin/yak" "/Applications/Rhino 9 WIP.app/Contents/Resources/bin/yak" "$(command -v yak || true)"; do
    [ -n "$c" ] && [ -x "$c" ] && YAK="$c" && break
  done
fi
[ -z "$YAK" ] && { echo "yak not found - set YAK=/path/to/yak"; exit 1; }

(cd "$STAGE" && "$YAK" build --platform any && mv ./*.yak ..)
ls -1 dist/*.yak

if [ "${1:-}" = "--push" ]; then
  "$YAK" push dist/*.yak
fi
