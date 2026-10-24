"""Writes a synthetic avatar pack (docs/avatar-pack-format.md) for testing avatar-characters
without the Unity exporter.

Every frame is a coloured stand-in figure labelled with its direction (row) and frame number,
with a nose showing where it faces. Gun animations also show their (gun, moving) direction
pair, so the row order can be checked in the game.

    python3 tools/avatar_pack/make_test_pack.py --out build/packs
    python3 tools/avatar_pack/make_test_pack.py --out build/packs --id test --flight --tint --zip
    python3 tools/avatar_pack/make_test_pack.py --out build/packs --id broken --broken missing-anim

The pack is written to <out>/avatar-pack-<id>/ (a folder Factorio loads as is), and with
--zip also to <out>/avatar-pack-<id>_<version>.zip. With --broken, the pack has one
deliberate mistake and the expected error is printed as "EXPECTED-ERROR: <text>".
"""

import argparse
import colorsys
import json
import math
import os
import shutil
import sys
import zipfile

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import lua_table  # noqa: E402
from avatar_pack.spec import DATA_LUA, DIRECTIONS, FACTORIO_VERSION, FLIGHT, REQUIRED, info_title, valid_id  # noqa: E402

# Frame counts per animation; --frames overrides them.
FRAMES = {
    "idle": 4, "idle-gun": 4, "run": 8, "run-gun": 8, "mine": 10, "corpse": 1,
    "hover": 4, "hover-gun": 4, "fly": 6, "fly-gun": 6, "take-off": 5,
}
IN_AIR = {"hover", "hover-gun", "fly", "fly-gun", "take-off"}
GUN = {"idle-gun", "run-gun", "hover-gun", "fly-gun"}
# (gun direction, moving direction) of each run-gun / fly-gun row, in eighths clockwise from north
GUN_ROWS = [(0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (1, 2), (1, 3), (2, 0), (2, 1), (2, 2), (2, 3),
            (3, 1), (3, 2), (3, 3), (3, 4), (4, 2), (4, 3), (4, 4)]
AIR_HEIGHT = 0.42  # tiles
SHADOW_ALPHA = 128

# kind -> (description, expected error text from avatar-characters)
BROKEN = {
    "format-version": ("format_version 99", "uses avatar pack format 99"),
    "missing-anim": ("no mine animation", 'is missing the required "mine" animation'),
    "bad-dirs": ("run has 4 directions", 'animation "run" must have 8 directions, not 4'),
    "partial-flight": ("flight = true without fly", 'has flight = true but no "fly" animation'),
    "outside-file": ("idle base points into __base__",
                     'animation "idle" layer "base" filename must be a file inside the pack'),
    "missing-mask": ("tintable without a mask on idle", 'animation "idle" is missing its "mask" layer'),
    "bad-split": ("run-gun base lists one file too few", 'animation "run-gun" layer "base" has'),
    "bad-timing": ("mine_impact_frame 99", "timing.mine_impact_frame must be a frame of the mine animation"),
    "too-many-frames": ("idle claims 256 frames", 'animation "idle" needs an integer frame_count between 1 and 255'),
    "title-too-long": ("an 81-character title", "needs a title of 1 to 80 characters"),
}


def font(size):
    try:
        return ImageFont.load_default(size=size)
    except TypeError:  # Pillow < 10.1
        return ImageFont.load_default()


def hue_color(anim, light=0.55):
    index = list(DIRECTIONS).index(anim)
    r, g, b = colorsys.hls_to_rgb(index / len(DIRECTIONS), light, 0.65)
    return int(r * 255), int(g * 255), int(b * 255), 255


def screen_vector(direction):
    """Unit vector on screen for a direction in eighths clockwise from north (y down)."""
    a = direction * math.pi / 4
    return math.sin(a), -math.cos(a) * 0.8


class Painter:
    """Draws the frames of one animation. Sizes are in pixels at the pack's resolution."""

    def __init__(self, ppt, tint):
        self.ppt = ppt
        self.s = ppt / 64
        self.tint = tint

    def px(self, v):
        return max(1, int(round(v * self.s)))

    def body_size(self, anim):
        if anim == "corpse":
            return self.px(112), self.px(64)
        return self.px(80), self.px(120)

    def shadow_size(self, anim):
        if anim == "corpse":
            return self.px(120), self.px(56)
        return self.px(100), self.px(48)

    def anchor(self, anim):
        """Pixel position of the ground point under the character in a body frame."""
        w, h = self.body_size(anim)
        if anim == "corpse":
            return w / 2, h / 2 + self.px(8)
        return w / 2, h - self.px(8)

    def body_shift(self, anim):
        w, h = self.body_size(anim)
        ax, ay = self.anchor(anim)
        if anim in IN_AIR:
            # the figure is drawn raised, the anchor stays on the ground below it
            ay += AIR_HEIGHT * self.ppt
        return [round((w / 2 - ax) / self.ppt, 5), round((h / 2 - ay) / self.ppt, 5)]

    def shadow_shift(self, anim):
        return [0.0, 0.0] if anim == "corpse" else [0.4, 0.05]

    def frame(self, anim, row, frame, frames):
        """Returns (base, mask, shadow, shadow_flipped) images of one frame."""
        if anim == "corpse":
            return self.corpse()
        w, h = self.body_size(anim)
        base = Image.new("RGBA", (w, h))
        mask = Image.new("RGBA", (w, h))
        d = ImageDraw.Draw(base)
        md = ImageDraw.Draw(mask)
        if anim in ("run-gun", "fly-gun"):
            gun, moving = GUN_ROWS[row]
            facing = gun
            label = f"{gun}/{moving}"
        else:
            facing = moving = row
            gun = row if anim in GUN else None
            label = str(row)
        phase = frame / frames
        bob = math.sin(phase * 2 * math.pi) * self.px(3)
        feet_y = h - self.px(8)
        cx = w / 2
        # legs: two feet stepping along the moving direction while running or flying
        mx, my = screen_vector(moving)
        stride = math.sin(phase * 2 * math.pi) * self.px(10) if anim in ("run", "run-gun", "fly", "fly-gun") else 0
        for side, sign in ((-1, 1), (1, -1)):
            fx = cx + side * self.px(9) + mx * stride * sign
            fy = feet_y - self.px(4) + my * stride * sign
            d.ellipse([fx - self.px(7), fy - self.px(4), fx + self.px(7), fy + self.px(4)], fill=(40, 40, 40, 255))
        # body
        top = self.px(34) + bob
        bottom = feet_y - self.px(6) + bob * 0.5
        body = [cx - self.px(22), top, cx + self.px(22), bottom]
        colour = hue_color(anim)
        if self.tint:
            # the tinted mask covers the body; the base below it is dark
            d.rounded_rectangle(body, radius=self.px(14), fill=(60, 60, 60, 255))
            md.rounded_rectangle(body, radius=self.px(14), fill=(235, 235, 235, 255))
            md.rounded_rectangle([body[0] + self.px(6), body[1] + self.px(6), body[2] - self.px(6),
                                  body[1] + self.px(18)], radius=self.px(6), fill=(255, 255, 255, 255))
        else:
            d.rounded_rectangle(body, radius=self.px(14), fill=colour)
        # head with a nose pointing where the figure faces
        hx, hy = cx, top - self.px(14)
        r = self.px(16)
        d.ellipse([hx - r, hy - r, hx + r, hy + r], fill=(245, 225, 200, 255), outline=(0, 0, 0, 255))
        fx, fy = screen_vector(facing)
        nx, ny = hx + fx * r, hy + fy * r
        d.ellipse([nx - self.px(5), ny - self.px(5), nx + self.px(5), ny + self.px(5)], fill=(200, 30, 30, 255))
        if gun is not None:
            gx, gy = screen_vector(gun)
            sx, sy = cx, top + self.px(22)
            gun_line = [sx, sy, sx + gx * self.px(34), sy + gy * self.px(34)]
            d.line(gun_line, fill=(20, 20, 20, 255), width=self.px(5))
            md.line(gun_line, fill=(0, 0, 0, 0), width=self.px(5))  # keep the gun out of the tinted layer
        if anim == "mine":
            # pickaxe swinging down towards the impact frame
            a = -math.pi * 0.8 + phase * math.pi * 1.2
            sx, sy = cx + self.px(18), top + self.px(20)
            pick_line = [sx, sy, sx + math.cos(a) * self.px(30), sy + math.sin(a) * self.px(30)]
            d.line(pick_line, fill=(110, 80, 40, 255), width=self.px(4))
            md.line(pick_line, fill=(0, 0, 0, 0), width=self.px(4))
        # labels: the direction (row label, in eighths of a turn from north counted from 0: east is 2) and
        # the frame, counted from 1 like Factorio's frame numbers
        big, small = font(self.px(16)), font(self.px(11))
        d.text((cx, (top + bottom) / 2 - self.px(6)), label, fill=(0, 0, 0, 255), font=big, anchor="mm")
        d.text((cx, (top + bottom) / 2 + self.px(12)), f"f{frame + 1}", fill=(0, 0, 0, 255), font=small, anchor="mm")
        if self.tint:
            # labels must also show on the tinted layer, which is drawn over the base
            md.text((cx, (top + bottom) / 2 - self.px(6)), label, fill=(0, 0, 0, 255), font=big, anchor="mm")
            md.text((cx, (top + bottom) / 2 + self.px(12)), f"f{frame + 1}", fill=(0, 0, 0, 255), font=small,
                    anchor="mm")
        shadow = self.shadow(anim, 1.0 + 0.1 * math.sin(phase * 2 * math.pi), hole=False, frame=frame)
        flipped = self.shadow(anim, 1.0 + 0.1 * math.sin(phase * 2 * math.pi), hole=True, frame=frame)
        return base, mask, shadow, flipped

    def shadow(self, anim, stretch, hole, frame=None):
        w, h = self.shadow_size(anim)
        img = Image.new("RGBA", (w, h))
        d = ImageDraw.Draw(img)
        rx, ry = w / 2 * 0.9 * min(stretch, 1.1) / 1.1, h / 2 * 0.8
        d.ellipse([w / 2 - rx, h / 2 - ry, w / 2 + rx, h / 2 + ry], fill=(0, 0, 0, SHADOW_ALPHA))
        if hole:
            # the flipped shadow has a gap, so it can be told apart from the normal one
            d.rectangle([w / 2 - self.px(4), 0, w / 2 + self.px(4), h], fill=(0, 0, 0, 0))
        if frame is not None:
            # the frame number cut out of the shadow, to see whether the game draws the shadow layer in
            # step with the body (whose label shows the same number)
            d.text((w / 2 + self.px(22), h / 2), f"{frame + 1}", fill=(0, 0, 0, 0), font=font(self.px(16)),
                   anchor="mm")
        return img

    def corpse(self):
        w, h = self.body_size("corpse")
        base = Image.new("RGBA", (w, h))
        mask = Image.new("RGBA", (w, h))
        d = ImageDraw.Draw(base)
        body = [self.px(8), self.px(16), w - self.px(30), h - self.px(12)]
        if self.tint:
            d.rounded_rectangle(body, radius=self.px(12), fill=(60, 60, 60, 255))
            ImageDraw.Draw(mask).rounded_rectangle(body, radius=self.px(12), fill=(235, 235, 235, 255))
        else:
            d.rounded_rectangle(body, radius=self.px(12), fill=hue_color("corpse", 0.4))
        r = self.px(14)
        hx, hy = w - self.px(22), h / 2
        d.ellipse([hx - r, hy - r, hx + r, hy + r], fill=(245, 225, 200, 255), outline=(0, 0, 0, 255))
        d.text((w / 2 - self.px(10), h / 2), "RIP", fill=(0, 0, 0, 255), font=font(self.px(14)), anchor="mm")
        if self.tint:
            ImageDraw.Draw(mask).text((w / 2 - self.px(10), h / 2), "RIP", fill=(0, 0, 0, 255),
                                      font=font(self.px(14)), anchor="mm")
        shadow = self.shadow("corpse", 1.0, hole=False)
        return base, mask, shadow, shadow


def write_layer(folder, mod_name, anim, layer, images, frames, max_sheet):
    """Packs images[row][frame] into one sheet, or several split by rows or, for a row too wide, by
    frames. Returns the layer table."""
    rows = len(images)
    w, h = images[0][0].size
    wide, tall = frames * w > max_sheet, rows * h > max_sheet
    if w > max_sheet or h > max_sheet or (wide and tall):
        raise SystemExit(f"{anim}-{layer}: {rows} x {frames} frames of {w}x{h} px do not fit --max-sheet {max_sheet}")
    lines = max_sheet // h if tall else rows
    per_file = max_sheet // w if wide else frames
    if wide:
        chunks = [(range(rows), range(start, min(start + per_file, frames))) for start in range(0, frames, per_file)]
    else:
        chunks = [(range(start, min(start + lines, rows)), range(frames)) for start in range(0, rows, lines)]
    paths = []
    for n, (chunk_rows, chunk_frames) in enumerate(chunks, 1):
        sheet = Image.new("RGBA", (len(chunk_frames) * w, len(chunk_rows) * h))
        for y, row in enumerate(chunk_rows):
            for x, f in enumerate(chunk_frames):
                sheet.paste(images[row][f], (x * w, y * h))
        name = f"{anim}-{layer}.png" if len(chunks) == 1 else f"{anim}-{layer}-{n}.png"
        sheet.save(os.path.join(folder, "graphics", name), optimize=True)
        paths.append(f"__{mod_name}__/graphics/{name}")
    info = {"width": w, "height": h}
    if len(paths) == 1:
        info["filename"] = paths[0]
    elif wide:
        info["filenames"] = paths
        info["frames_per_file"] = per_file
    else:
        info["filenames"] = paths
        info["lines_per_file"] = lines
    return info


def write_icon(path, size, tint, title):
    img = Image.new("RGBA", (size, size))
    d = ImageDraw.Draw(img)
    s = size / 64
    d.ellipse([8 * s, 4 * s, 56 * s, 52 * s], fill=(245, 225, 200, 255), outline=(0, 0, 0, 255), width=max(1, int(2 * s)))
    d.rounded_rectangle([14 * s, 44 * s, 50 * s, 63 * s], radius=6 * s,
                        fill=(200, 200, 200, 255) if tint else hue_color("idle"))
    d.text((32 * s, 28 * s), (title[:1] or "?").upper(), fill=(0, 0, 0, 255), font=font(int(28 * s)), anchor="mm")
    img.save(path, optimize=True)


def build(args):
    mod_name = f"avatar-pack-{args.id}"
    flight = args.flight or args.broken == "partial-flight"
    tint = args.tint or args.broken == "missing-mask"
    max_sheet = 1024 if args.broken == "bad-split" else args.max_sheet

    folder = os.path.join(args.out, mod_name)
    if os.path.exists(folder):
        shutil.rmtree(folder)
    for sub in ("graphics", "prototypes", os.path.join("locale", "en")):
        os.makedirs(os.path.join(folder, sub))

    animations = {}
    names = list(REQUIRED) + (list(FLIGHT) if flight else [])
    for anim in names:
        painter = Painter(32 if anim in args.half else args.ppt, tint)
        dirs, frames = DIRECTIONS[anim], args.frames.get(anim, FRAMES[anim])
        cells = [[painter.frame(anim, row, f, frames) for f in range(frames)] for row in range(dirs)]
        layer_index = {"base": 0, "shadow": 2}
        if tint:
            layer_index["mask"] = 1
        if anim == "run-gun":
            layer_index["shadow-flipped"] = 3
        layers = {}
        for layer, i in layer_index.items():
            info = write_layer(folder, mod_name, anim, layer, [[c[i] for c in row] for row in cells], frames,
                               max_sheet)
            info["shift"] = painter.body_shift(anim) if i in (0, 1) else painter.shadow_shift(anim)
            layers[layer] = info
        animations[anim] = {"direction_count": dirs, "frame_count": frames, "layers": layers}
        if anim in args.half and args.ppt != 32:
            animations[anim]["pixels_per_tile"] = 32

    write_icon(os.path.join(folder, "graphics", "icon.png"), args.ppt, tint, args.title)
    thumb = Image.new("RGBA", (144, 144), (40, 40, 48, 255))
    icon = Image.open(os.path.join(folder, "graphics", "icon.png")).resize((112, 112), Image.LANCZOS)
    thumb.alpha_composite(icon, (16, 16))
    thumb.save(os.path.join(folder, "thumbnail.png"), optimize=True)

    pack = {
        "format_version": 1,
        "id": args.id,
        "title": args.title,
        "author": args.author,
        "generator": {"name": "make_test_pack", "version": "1"},
        "icon": {"filename": f"__{mod_name}__/graphics/icon.png", "size": args.ppt},
        "pixels_per_tile": args.ppt,
        "tintable": tint,
        "flight": flight,
        "animations": animations,
    }
    if not args.default_timing:
        pack["timing"] = {
            "distance_per_frame": 0.15,
            "mine_impact_frame": 6,
            "left_foot_frames": [1],
            "right_foot_frames": [5],
            "animation_speed": {"idle": 0.08, "run": 0.5},
        }

    if args.broken == "format-version":
        pack["format_version"] = 99
    elif args.broken == "missing-anim":
        del animations["mine"]
    elif args.broken == "bad-dirs":
        animations["run"]["direction_count"] = 4
    elif args.broken == "partial-flight":
        del animations["fly"]
    elif args.broken == "outside-file":
        animations["idle"]["layers"]["base"]["filename"] = "__base__/graphics/entity/character/level1_idle.png"
    elif args.broken == "missing-mask":
        del animations["idle"]["layers"]["mask"]
    elif args.broken == "bad-split":
        animations["run-gun"]["layers"]["base"]["filenames"].pop()
    elif args.broken == "too-many-frames":
        animations["idle"]["frame_count"] = 256
    elif args.broken == "title-too-long":
        pack["title"] = "x" * 81
    elif args.broken == "bad-timing":
        pack.setdefault("timing", {})["mine_impact_frame"] = 99

    with open(os.path.join(folder, "prototypes", "avatar-pack.lua"), "w", newline="\n") as fh:
        fh.write("-- Written by tools/avatar_pack/make_test_pack.py: a synthetic test pack.\n")
        fh.write("return " + lua_table.dumps(pack) + "\n")
    with open(os.path.join(folder, "data.lua"), "w", newline="\n") as fh:
        fh.write(DATA_LUA)
    info = {
        "name": mod_name,
        "version": args.version,
        "title": info_title(args.title),
        "author": args.author,
        "factorio_version": FACTORIO_VERSION,
        "description": "Synthetic avatar pack for testing Avatar Characters.",
        "dependencies": ["avatar-characters >= 0.1.0"],
    }
    with open(os.path.join(folder, "info.json"), "w", newline="\n") as fh:
        json.dump(info, fh, indent=2)
        fh.write("\n")
    with open(os.path.join(folder, "locale", "en", "locale.cfg"), "w", newline="\n") as fh:
        fh.write(f"[mod-name]\n{mod_name}=Avatar pack: {args.title}\n\n"
                 f"[mod-description]\n{mod_name}=Synthetic avatar pack for testing Avatar Characters.\n")
    with open(os.path.join(folder, "README.txt"), "w", newline="\n") as fh:
        fh.write("Synthetic avatar pack written by tools/avatar_pack/make_test_pack.py.\n"
                 "It is not made from a real avatar and may be shared freely.\n")

    print(f"wrote {folder}")
    if args.zip:
        top = f"{mod_name}_{args.version}"
        zip_path = os.path.join(args.out, f"{top}.zip")
        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
            for root, _, files in sorted(os.walk(folder)):
                for name in sorted(files):
                    path = os.path.join(root, name)
                    zf.write(path, os.path.join(top, os.path.relpath(path, folder)))
        print(f"wrote {zip_path}")
    if args.broken:
        print(f"EXPECTED-ERROR: {BROKEN[args.broken][1]}")


def frame_counts(text):
    counts = {}
    for item in text.split(","):
        name, _, count = item.partition("=")
        if name not in DIRECTIONS or not count.isdigit() or not 1 <= int(count) <= 255:
            raise argparse.ArgumentTypeError(f"bad frame count {item!r}")
        counts[name] = int(count)
    return counts


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", help="folder to write the pack into")
    parser.add_argument("--list-broken", action="store_true", help="print the --broken kinds and exit")
    parser.add_argument("--id", default="test", help="pack id (default: test)")
    parser.add_argument("--title", default="Test Avatar")
    parser.add_argument("--author", default="make_test_pack")
    parser.add_argument("--version", default="1.0.0")
    parser.add_argument("--flight", action="store_true", help="include the flight animations")
    parser.add_argument("--tint", action="store_true", help="tintable, with mask layers")
    parser.add_argument("--ppt", type=int, choices=(64, 32), default=64, help="pixels per tile")
    parser.add_argument("--half", type=lambda t: set(t.split(",")), default=set(), metavar="ANIM,...",
                        help="draw these animations at 32 pixels per tile (half resolution, the same size in the game)")
    parser.add_argument("--max-sheet", type=int, default=4096,
                        help="largest sheet size in px; lower it to force split sheets")
    parser.add_argument("--default-timing", action="store_true", help="leave timing out of the pack")
    parser.add_argument("--frames", type=frame_counts, default={}, metavar="ANIM=N,...",
                        help="override frame counts, e.g. run=6,mine=8")
    parser.add_argument("--zip", action="store_true", help="also write the mod zip")
    parser.add_argument("--broken", choices=sorted(BROKEN), help="add one deliberate mistake: "
                        + "; ".join(f"{k}: {v[0]}" for k, v in sorted(BROKEN.items())))
    args = parser.parse_args(argv)
    if args.list_broken:
        print("\n".join(sorted(BROKEN)))
        return
    if not args.out:
        parser.error("--out is required")
    if not valid_id(args.id):
        parser.error("--id: lowercase letters, digits and -, 1 to 35 characters, not starting with - or corpse-")
    build(args)


if __name__ == "__main__":
    main()
