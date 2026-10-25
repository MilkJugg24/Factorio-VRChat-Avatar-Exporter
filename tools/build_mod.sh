#!/usr/bin/env bash
# Packs vrc-avatars/ into build/vrc-avatars_<version>.zip, ready to
# drop into Factorio's mods folder or upload to the mod portal.
set -euo pipefail
REPO="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(python3 -c "import json;print(json.load(open('$REPO/vrc-avatars/info.json'))['version'])")"
NAME="vrc-avatars_${VERSION}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
cp -r "$REPO/vrc-avatars" "$STAGE/$NAME"
mkdir -p "$REPO/build"
rm -f "$REPO/build/$NAME.zip"
(cd "$STAGE" && python3 -m zipfile -c "$REPO/build/$NAME.zip" "$NAME")
echo "built build/$NAME.zip"
