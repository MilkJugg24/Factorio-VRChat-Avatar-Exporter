#!/usr/bin/env bash
# Plays the in-game screenshot scenes (tests/vrc-avatars-screenshots) in a headless Factorio,
# which takes no screenshots and has no player, and checks what the scenes did: everyone walked,
# flew over the water (with Space Age) and left a corpse. Guards the scenes that
# tests/run_ingame_checks.ps1 plays in the real game.
#
#   tests/run_ingame_dryrun.sh /path/to/factorio [--space-age]
set -euo pipefail
FACTORIO_DIR="$(cd "$1" && pwd)"
DLC=false
[ "${2:-}" = "--space-age" ] && DLC=true
REPO="$(cd "$(dirname "$0")/.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

mkdir -p "$WORK/mods" "$WORK/config"
cp -r "$REPO/vrc-avatars" "$REPO/tests/vrc-avatars-screenshots" "$WORK/mods/"
python3 "$REPO/tools/avatar_pack/make_test_pack.py" --out "$WORK/mods" --id test --flight --tint > /dev/null
python3 - "$WORK/mods" "$DLC" <<'PY'
import json, os, sys
mods_dir, dlc = sys.argv[1], sys.argv[2] == "true"
mods = [{"name": "base", "enabled": True}]
mods += [{"name": n, "enabled": dlc} for n in ("space-age", "quality", "elevated-rails")]
for entry in sorted(os.listdir(mods_dir)):
    with open(os.path.join(mods_dir, entry, "info.json")) as f:
        mods.append({"name": json.load(f)["name"], "enabled": True})
json.dump({"mods": mods}, open(os.path.join(mods_dir, "mod-list.json"), "w"))
PY
# script-output goes to the work folder, not the Factorio install
printf '[path]\nread-data=__PATH__executable__/../../data\nwrite-data=%s\n' "$WORK" > "$WORK/config/config.ini"
FACTORIO=("$FACTORIO_DIR/bin/x64/factorio" --config "$WORK/config/config.ini" --mod-directory "$WORK/mods")
"${FACTORIO[@]}" --create "$WORK/checks.zip" > "$WORK/create.log" 2>&1 || { cat "$WORK/create.log"; exit 1; }
"${FACTORIO[@]}" --benchmark "$WORK/checks.zip" --benchmark-ticks 1800 > "$WORK/run.log" 2>&1 || true
grep -q "AVATAR-CHECKS: DONE" "$WORK/run.log" || { cat "$WORK/run.log"; echo "the scenes did not finish" >&2; exit 1; }

python3 - "$WORK/script-output/avatar-checks/manifest.json" "$DLC" <<'PY'
import json, sys
manifest, dlc = json.load(open(sys.argv[1])), sys.argv[2] == "true"
problems = []
for run in manifest["runs"]:
    name = run["scene"]
    for s in run["subjects"]:
        label = s.get("label")
        if name.startswith("run") and not run["solo"] and s.get("moved", 0) < 5:
            problems.append(f"{name}: {label} hardly moved ({s.get('moved')})")
        if name.startswith("fly") and dlc and not run["solo"] and not s.get("flew"):
            problems.append(f"{name}: {label} never took off")
        if dlc and not run["solo"] and s.get("landed") is False:
            problems.append(f"{name}: {label} never landed")
        if name == "corpse" and not s.get("corpse"):
            problems.append(f"{name}: {label} left no corpse")
labels = {s.get("label") for run in manifest["runs"] for s in run["subjects"]}
for expected in ("engineer", "test"):
    if expected not in labels:
        problems.append(f"no {expected} in the scenes")
if problems:
    sys.exit("\n".join(problems))
PY
echo "INGAME-DRYRUN: PASS ($([ "$DLC" = true ] && echo "with Space Age" || echo "base game"))"
