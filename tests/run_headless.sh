#!/usr/bin/env bash
# Loads the mod in a headless Factorio server and runs the tests in
# tests/vrc-avatars-test.
#
#   tests/run_headless.sh /path/to/factorio [options]
#
#   --space-age     also enable Space Age, Quality and Elevated Rails
#   --test-packs    add two synthetic avatar packs (tools/avatar_pack/make_test_pack.py),
#                   test them, then load the save again without them, as when a
#                   pack is removed
#   --broken-packs  check that each deliberately broken pack stops the game from
#                   loading with the expected error
#   --pack PATH     add an avatar pack or bundle (folder or zip) to the test; may be repeated
#   --no-armor-flight  turn off the startup setting "Every armor flies (wings)" (on by default)
#   --swap-to PATH  then load the save again with this pack or bundle instead of the --pack ones,
#                   as a server does when it moves its players' packs into one bundle
#
# /path/to/factorio is an unpacked Factorio (headless works) directory.
set -euo pipefail
FACTORIO_DIR="$(cd "$1" && pwd)"
shift
DLC=false
TEST_PACKS=false
BROKEN_PACKS=false
PACKS=()
SWAP_TO=""
ARMOR_FLIGHT=true
while [ $# -gt 0 ]; do
  case "$1" in
    --space-age) DLC=true ;;
    --test-packs) TEST_PACKS=true ;;
    --broken-packs) BROKEN_PACKS=true ;;
    --no-armor-flight) ARMOR_FLIGHT=false ;;
    --pack) PACKS+=("$(realpath "$2")"); shift ;;
    --swap-to) SWAP_TO="$(realpath "$2")"; shift ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done
REPO="$(cd "$(dirname "$0")/.." && pwd)"
FACTORIO="$FACTORIO_DIR/bin/x64/factorio"
MAKE_PACK="$REPO/tools/avatar_pack/make_test_pack.py"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
if [ "$DLC" = true ]; then LABEL="with Space Age"; else LABEL="base game"; fi
if [ "$ARMOR_FLIGHT" = false ]; then LABEL="$LABEL, no flying armor"; fi

fail() {
  echo "$1" >&2
  exit 1
}

# new_mods_dir DIR: the mod, the test mod and a mod-list that enables every mod in DIR
new_mods_dir() {
  mkdir -p "$1"
  cp -r "$REPO/vrc-avatars" "$1/vrc-avatars"
  cp -r "$REPO/tests/vrc-avatars-test" "$1/vrc-avatars-test"
  if [ "$ARMOR_FLIGHT" = false ]; then
    # a startup setting can't be set on the command line: a tiny mod changes its default
    mkdir -p "$1/vrc-avatars-flight-off"
    cat > "$1/vrc-avatars-flight-off/info.json" <<'JSON'
{"name": "vrc-avatars-flight-off", "version": "0.1.0", "title": "No flying armor (test)", "author": "VRC Avatars",
 "factorio_version": "2.1", "dependencies": ["vrc-avatars"]}
JSON
    echo 'data.raw["bool-setting"]["vrc-avatars-armor-flight"].default_value = false' \
      > "$1/vrc-avatars-flight-off/settings-updates.lua"
  fi
}

# write_mod_list DIR: enables base, the DLC (or not) and every mod in DIR
write_mod_list() {
  python3 - "$1" "$DLC" <<'PY'
import json, os, sys, zipfile
mods_dir, dlc = sys.argv[1], sys.argv[2] == "true"
names = []
for entry in sorted(os.listdir(mods_dir)):
    path = os.path.join(mods_dir, entry)
    if os.path.isdir(path):
        info = json.load(open(os.path.join(path, "info.json")))
    elif entry.endswith(".zip"):
        with zipfile.ZipFile(path) as zf:
            info = json.loads(zf.read(next(n for n in zf.namelist() if n.count("/") == 1 and n.endswith("/info.json"))))
    else:
        continue
    names.append(info["name"])
mods = [{"name": "base", "enabled": True}]
mods += [{"name": n, "enabled": dlc} for n in ("space-age", "quality", "elevated-rails")]
mods += [{"name": n, "enabled": True} for n in names]
json.dump({"mods": mods}, open(os.path.join(mods_dir, "mod-list.json"), "w"))
PY
}

# run_benchmark MODS SAVE LOG EXPECTED: runs a few ticks and checks the test passed
run_benchmark() {
  "$FACTORIO" --mod-directory "$1" --benchmark "$2" --benchmark-ticks 5 > "$3" 2>&1 || true
  if grep -qF "$4" "$3"; then
    echo "$4 ($LABEL)"
  else
    cat "$3"
    fail "expected \"$4\""
  fi
}

if [ "$BROKEN_PACKS" = true ]; then
  # a plain assignment, so a failing generator stops the script (set -e)
  kinds="$(python3 "$MAKE_PACK" --list-broken)"
  [ -n "$kinds" ] || fail "make_test_pack.py lists no broken pack kinds"
  for kind in $kinds; do
    dir="$WORK/broken-$kind"
    new_mods_dir "$dir/mods"
    expected="$(python3 "$MAKE_PACK" --out "$dir/mods" --id broken --broken "$kind" | sed -n 's/^EXPECTED-ERROR: //p')"
    [ -n "$expected" ] || fail "no expected error for $kind"
    if python3 "$REPO/tools/validate_sprites.py" --pack "$dir/mods/vrc-avatar-broken" > "$dir/validate.log" 2>&1; then
      fail "BROKEN-PACK $kind: tools/validate_sprites.py accepted it"
    fi
    grep -qF "$expected" "$dir/validate.log" \
      || { cat "$dir/validate.log"; fail "BROKEN-PACK $kind: tools/validate_sprites.py did not report \"$expected\""; }
    write_mod_list "$dir/mods"
    if "$FACTORIO" --mod-directory "$dir/mods" --create "$dir/test.zip" > "$dir/create.log" 2>&1; then
      fail "BROKEN-PACK $kind: Factorio loaded it"
    fi
    grep -qF "$expected" "$dir/create.log" || { cat "$dir/create.log"; fail "BROKEN-PACK $kind: expected \"$expected\""; }
    echo "BROKEN-PACK $kind: PASS ($LABEL)"
    rm -rf "$dir"
  done
fi

MODS="$WORK/mods"
new_mods_dir "$MODS"
PACK_NAMES=()
if [ "$TEST_PACKS" = true ]; then
  python3 "$MAKE_PACK" --out "$MODS" --id test --title "Test Avatar" --flight --flap --tint --max-sheet 1024 --frames idle=16 --half run > /dev/null
  # a 30-character title of 90 bytes, and default timing scaled to 6 run / 8 mine frames, no sprint
  # (as packs made before it); installed as a zip only, like a downloaded pack
  python3 "$MAKE_PACK" --out "$WORK/packs" --id small --title "$(printf '竜%.0s' $(seq 30))" --ppt 32 \
    --default-timing --frames run=6,mine=8 --no-sprint --zip > /dev/null
  cp "$WORK/packs/vrc-avatar-small_1.0.0.zip" "$MODS/"
  PACK_NAMES+=(vrc-avatar-test vrc-avatar-small_1.0.0.zip)
fi
EXTRA_NAMES=()
for pack in "${PACKS[@]+"${PACKS[@]}"}"; do
  cp -r "$pack" "$MODS/"
  PACK_NAMES+=("$(basename "$pack")")
  EXTRA_NAMES+=("$(basename "$pack")")
done
for name in "${PACK_NAMES[@]+"${PACK_NAMES[@]}"}"; do
  python3 "$REPO/tools/validate_sprites.py" --pack "$MODS/$name" || fail "$name: tools/validate_sprites.py failed"
done
write_mod_list "$MODS"

"$FACTORIO" --mod-directory "$MODS" --create "$WORK/test.zip" > "$WORK/create.log" 2>&1 \
  || { cat "$WORK/create.log"; exit 1; }
if [ "$TEST_PACKS" = true ]; then
  run_benchmark "$MODS" "$WORK/test.zip" "$WORK/run.log" "AVATAR-TEST: PASS (with avatar packs)"
  # remove the packs from the save, as a player would after /avatar-revert-all
  for name in "${PACK_NAMES[@]}"; do
    rm -rf "${MODS:?}/$name"
  done
  write_mod_list "$MODS"
  run_benchmark "$MODS" "$WORK/test.zip" "$WORK/removed.log" "AVATAR-TEST: PASS (avatar packs removed)"
else
  run_benchmark "$MODS" "$WORK/test.zip" "$WORK/run.log" "AVATAR-TEST: PASS"
fi

if [ -n "$SWAP_TO" ]; then
  # the same save with the --pack mods replaced: characters of packs still installed must survive
  for name in "${EXTRA_NAMES[@]+"${EXTRA_NAMES[@]}"}"; do
    rm -rf "${MODS:?}/$name"
  done
  cp -r "$SWAP_TO" "$MODS/"
  python3 "$REPO/tools/validate_sprites.py" --pack "$MODS/$(basename "$SWAP_TO")" > /dev/null \
    || fail "$(basename "$SWAP_TO"): tools/validate_sprites.py failed"
  write_mod_list "$MODS"
  run_benchmark "$MODS" "$WORK/test.zip" "$WORK/swapped.log" "AVATAR-TEST: PASS"
  echo "(after swapping the packs for $(basename "$SWAP_TO"))"
fi
