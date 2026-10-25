#!/usr/bin/env bash
# End-to-end test of the avatar exporter's Unity-free Core: the pack harness poses a synthetic
# humanoid rig, renders it with the software stick-figure renderer and writes an avatar pack
# exactly like the Unity exporter will; the pack is then checked by tools/validate_sprites.py,
# loaded and exercised in a headless Factorio by tests/run_headless.sh, and previewed as GIFs. A
# second pack is made and both are combined into a bundle, which must load in place of the packs
# without losing the characters a save holds.
#
#   tests/run_avatar_pipeline.sh /path/to/factorio [--space-age] [--rig generic|chibi|minimal]
#
# Needs the .NET 8 SDK (dotnet on PATH or in ~/.dotnet) and python3 with Pillow.
# Preview GIFs are left in build/pipeline-preview/.
set -euo pipefail
FACTORIO_DIR="$(cd "$1" && pwd)"
shift
HEADLESS_ARGS=()
RIG=generic
while [ $# -gt 0 ]; do
  case "$1" in
    --space-age) HEADLESS_ARGS+=(--space-age) ;;
    --rig) RIG="$2"; shift ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done
REPO="$(cd "$(dirname "$0")/.." && pwd)"
if ! command -v dotnet > /dev/null && [ -x "$HOME/.dotnet/dotnet" ]; then
  export PATH="$HOME/.dotnet:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

echo "== building the pack with the harness ($RIG rig)"
dotnet run --project "$REPO/unity/dotnet/PackHarness" -c Release -- \
  --out "$WORK" --id "harness-$RIG" --title "Harness $RIG" --rig "$RIG" --zip
ZIP="$(ls "$WORK"/vrc-avatar-harness-"$RIG"_*.zip)"

echo "== checking it"
python3 "$REPO/tools/validate_sprites.py" --pack "$WORK/vrc-avatar-harness-$RIG" "$ZIP"

echo "== loading it in Factorio"
"$REPO/tests/run_headless.sh" "$FACTORIO_DIR" "${HEADLESS_ARGS[@]+"${HEADLESS_ARGS[@]}"}" --pack "$ZIP"

echo "== a second pack, and both combined into a bundle"
dotnet run --project "$REPO/unity/dotnet/PackHarness" -c Release -- \
  --out "$WORK" --id harness-second --title "Harness second" --rig chibi --no-flight --zip > /dev/null
SECOND="$(ls "$WORK"/vrc-avatar-harness-second_*.zip)"
dotnet run --project "$REPO/unity/dotnet/PackHarness" -c Release -- bundle --out "$WORK" --name "Harness bundle" "$ZIP" "$SECOND"
BUNDLE="$(ls "$WORK"/avatar-bundle-harness-bundle_*.zip)"
python3 "$REPO/tools/validate_sprites.py" --pack "$BUNDLE"

echo "== a save made with both packs, loaded again with the bundle instead"
"$REPO/tests/run_headless.sh" "$FACTORIO_DIR" "${HEADLESS_ARGS[@]+"${HEADLESS_ARGS[@]}"}" --pack "$ZIP" --pack "$SECOND" \
  --swap-to "$BUNDLE"

echo "== previews"
python3 "$REPO/tools/avatar_pack/preview.py" "$ZIP" --out "$REPO/build/pipeline-preview" > /dev/null
echo "preview GIFs in build/pipeline-preview/"
echo "AVATAR-PIPELINE: PASS"
