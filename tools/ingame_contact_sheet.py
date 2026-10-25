#!/usr/bin/env python3
"""Turns the in-game screenshots of tests/run_ingame_checks.ps1 into GIFs, strips and a report.

    python3 tools/ingame_contact_sheet.py ingame-checks/<date>

Reads <folder>/manifest.json (written by tests/vrc-avatars-screenshots) and writes, per scene:

- <scene>.gif: the screenshots as an animation at the game's real speed, every subject at once,
  each lane labelled; runs of solo scenes (shooting, mining) are stacked so they play together;
- <scene>-strip.png: the same frames side by side, for looking at single frames;

and REPORT.md: what each run did (distance walked, shots fired, ore mined, flight, corpse) with
anything suspicious marked, and links to the GIFs. Needs Pillow.
"""

import argparse
import json
import os

from PIL import Image, ImageDraw

TICKS_PER_SECOND = 60
STRIP_COLUMNS = 16
BACKGROUND = (30, 30, 30)
TEXT = (255, 220, 80)


def load(folder, frame):
    path = os.path.join(folder, frame["file"])
    return Image.open(path).convert("RGB") if os.path.exists(path) else None


def label_lanes(image, labels, ppt, lane_tiles, vertical_lanes, room=0):
    """Writes each subject's label next to its lane (lanes are centred on the screenshot, but for
    `room` tiles of extra view above them)."""
    draw = ImageDraw.Draw(image)
    n = len(labels)
    for i, label in enumerate(labels):
        offset = (i + 1 - (n + 1) / 2) * lane_tiles
        if vertical_lanes:  # lanes stacked top to bottom (travel east or west)
            y = image.height / 2 + (offset + 0.75 + room / 2) * ppt - 1.6 * ppt
            draw.text((4, max(0, y)), label, fill=TEXT)
        else:  # lanes side by side (travel north or south)
            x = image.width / 2 + offset * ppt - 0.9 * ppt
            draw.text((max(0, x), 4), label, fill=TEXT)
    return image


def stack(images, labels):
    """Solo runs: one image per subject, stacked top to bottom, each labelled."""
    width = max(i.width for i in images)
    height = sum(i.height for i in images)
    out = Image.new("RGB", (width, height), BACKGROUND)
    y = 0
    draw = ImageDraw.Draw(out)
    for image, label in zip(images, labels):
        out.paste(image, (0, y))
        draw.text((4, y + 4), label, fill=TEXT)
        y += image.height
    return out


def strip(frames):
    columns = min(STRIP_COLUMNS, len(frames))
    rows = (len(frames) + columns - 1) // columns
    w, h = frames[0].size
    out = Image.new("RGB", (w * columns, h * rows), BACKGROUND)
    for i, frame in enumerate(frames):
        out.paste(frame, ((i % columns) * w, (i // columns) * h))
    return out


def scene_frames(folder, runs, ppt, lane_tiles):
    """The frames of one scene, ready to animate, or None when no screenshot exists."""
    first = runs[0]
    # older manifests have no "travel": the scene names held the direction then
    travel = first.get("travel") or ("east" if "east" in first["scene"] or "west" in first["scene"] else "south")
    vertical = travel in ("east", "west")
    if not first["solo"]:
        labels = [s["label"] for s in first["subjects"]]
        frames = [load(folder, f) for f in first["frames"] or []]
        frames = [label_lanes(f, labels, ppt, lane_tiles, vertical, first.get("room", 0))
                  for f in frames if f is not None]
        return frames or None
    runs = [r for r in runs if r["frames"]]
    if not runs:
        return None
    count = min(len(r["frames"]) for r in runs)
    frames = []
    for i in range(count):
        images = [load(folder, r["frames"][i]) for r in runs]
        if any(image is None for image in images):
            return frames or None
        frames.append(stack(images, [r["subjects"][0]["label"] for r in runs]))
    return frames or None


def problems(run):
    """Anything in a run's results that suggests the scene did not do what it should."""
    found = []
    name = run["scene"]
    # shooting the submachine gun slows its shooter to 30% (movement_slow_down_factor 0.7)
    least = 2 if "gun" in name else 5
    for s in run["subjects"]:
        label = s.get("label")
        if name.startswith("run") and s.get("moved", 0) < least:
            found.append(f"{label} hardly moved ({s.get('moved')} tiles)")
        if "gun" in name and s.get("shots_fired") == 0:
            found.append(f"{label} fired no shots")
        if name.startswith("mine") and s.get("ore_mined") == 0:
            found.append(f"{label} mined nothing")
        if name.startswith("fly") and s.get("flew") is False:
            found.append(f"{label} never took off")
        if s.get("landed") is False:
            found.append(f"{label} never landed")
        if name == "corpse" and s.get("corpse") is False:
            found.append(f"{label} left no corpse")
    return found


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("folder", help="the copied script-output/avatar-checks folder")
    args = parser.parse_args()
    folder = args.folder
    with open(os.path.join(folder, "manifest.json"), encoding="utf-8") as f:
        manifest = json.load(f)
    ppt = manifest["pixels_per_tile"]
    lane_tiles = manifest["lane_tiles"]
    scenes = {}
    for run in manifest["runs"]:
        scenes.setdefault(run["scene"], []).append(run)

    report = ["# In-game checks", "", "Mods: " + ", ".join(f"{k} {v}" for k, v in sorted(manifest["mods"].items())), ""]
    for name, runs in scenes.items():
        report.append(f"## {name}")
        report.append("")
        frames = scene_frames(folder, runs, ppt, lane_tiles)
        if frames:
            every = runs[0]["every"]
            frames[0].save(os.path.join(folder, f"{name}.gif"), save_all=True, append_images=frames[1:],
                           duration=round(every * 1000 / TICKS_PER_SECOND), loop=0)
            strip(frames).save(os.path.join(folder, f"{name}-strip.png"))
            report.append(f"![{name}]({name}.gif) ({len(frames)} frames, one every {every} ticks; "
                          f"[all frames]({name}-strip.png))")
        else:
            report.append("No screenshots.")
        report.append("")
        for run in runs:
            for note in run["notes"] or []:
                report.append(f"- note: {note}")
            for s in run["subjects"]:
                facts = ", ".join(f"{k} {v}" for k, v in s.items() if k not in ("label", "prototype"))
                report.append(f"- {s.get('label')}: {facts or 'no results'}")
            for problem in problems(run):
                report.append(f"- **problem:** {problem}")
        report.append("")
    window = manifest.get("window")
    if window:
        report += ["## The avatar window", ""]
        if os.path.exists(os.path.join(folder, "window", "window.png")):
            report += ["![the avatar window](window/window.png)", ""]
        report.append("- " + ", ".join(f"{k}: {v}" for k, v in sorted(window.items())))
        if window.get("window_open") is False:
            report.append("- **problem:** the window did not open")
        if window.get("pack") and window.get("character") != "vrc-avatar-" + window["pack"]:
            report.append(f"- **problem:** the player is {window.get('character')}, not the pack's character")
        report.append("")
    with open(os.path.join(folder, "REPORT.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(report))
    print(os.path.join(folder, "REPORT.md"))


if __name__ == "__main__":
    main()
