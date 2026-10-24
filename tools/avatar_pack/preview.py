"""Animated previews of an avatar pack (docs/avatar-pack-format.md), so its poses can be checked
without starting Factorio or Unity.

Writes one GIF per animation with every direction side by side, drawn the way Factorio places
the sprites: each frame at its shift from the entity position, the shadow under the character,
the mask layer tinted with the player colour, and the animation's speed. Sheet rows are labelled
with their direction (and the gun/moving pair for run-gun and fly-gun).

    python3 tools/avatar_pack/preview.py path/to/avatar-pack-mika_1.0.0.zip --out build/preview
    python3 tools/avatar_pack/preview.py path/to/pack --animation run --zoom 2
"""

import argparse
import math
import os
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import lua_table  # noqa: E402
from avatar_pack import spec  # noqa: E402
from avatar_pack.check import PackFiles  # noqa: E402

GUN_ROWS = [(0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (1, 2), (1, 3), (2, 0), (2, 1), (2, 2), (2, 3),
            (3, 1), (3, 2), (3, 3), (3, 4), (4, 2), (4, 3), (4, 4)]
DIRECTION_NAMES = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]
DEFAULT_SPEEDS = {
    "idle": 0.06, "idle-gun": 0.06, "run": 0.6, "run-gun": 0.6, "mine": 0.415, "corpse": 1,
    "hover": 0.27, "hover-gun": 0.27, "fly": 0.3, "fly-gun": 0.3, "take-off": 0.3,
}
GROUND = (74, 86, 58, 255)
PLAYER_COLOR = (0.869, 0.5, 0.130)  # Factorio's default player colour
SHADOW_STRENGTH = 0.55  # roughly how dark Factorio draws shadows
LABEL_HEIGHT = 14


class Pack:
    def __init__(self, path):
        self.files = PackFiles(path)
        self.data = lua_table.loads(self.files.read("prototypes/avatar-pack.lua").decode("utf-8"))
        self.prefix = f"__{spec.MOD_PREFIX}{self.data['id']}__/"
        self.ppt = self.data.get("pixels_per_tile", 64)
        self.speeds = dict(DEFAULT_SPEEDS)
        # as avatar-characters does: the default mine speed is for a 12-frame swing
        self.speeds["mine"] *= self.data["animations"]["mine"]["frame_count"] / 12
        self.speeds.update((self.data.get("timing") or {}).get("animation_speed") or {})
        self._sheets = {}

    def sheet(self, path):
        if path not in self._sheets:
            self._sheets[path] = self.files.image(path[len(self.prefix):]).convert("RGBA")
        return self._sheets[path]

    def frame(self, meta, layer, row, column):
        """The image of one frame of a layer and where its centre is, in pixels from the entity position."""
        info = meta["layers"][layer]
        w, h = info["width"], info["height"]
        if "filenames" in info and "frames_per_file" in info:
            per_file = info["frames_per_file"]
            sheet = self.sheet(info["filenames"][column // per_file])
            column %= per_file
        elif "filenames" in info:
            lines = info["lines_per_file"]
            sheet = self.sheet(info["filenames"][row // lines])
            row %= lines
        else:
            sheet = self.sheet(info["filename"])
        img = sheet.crop((column * w, row * h, (column + 1) * w, (row + 1) * h))
        zoom = self.zoom(meta)
        if zoom != 1:
            img = img.resize((round(w * zoom), round(h * zoom)), Image.LANCZOS)
        return img, (info["shift"][0] * self.ppt, info["shift"][1] * self.ppt)

    def zoom(self, meta):
        """How much larger an animation drawn at its own pixels_per_tile is shown, to match the pack's."""
        return self.ppt / meta.get("pixels_per_tile", self.ppt)


def tinted(mask, color):
    """The mask layer as Factorio's apply_runtime_tint draws it with an alpha 0.5 player colour."""
    r, g, b, a = mask.split()
    tint = [int(round(255 * c)) for c in color]
    out = Image.merge("RGBA", (r.point(lambda v: v * tint[0] // 255), g.point(lambda v: v * tint[1] // 255),
                               b.point(lambda v: v * tint[2] // 255), a))
    return out


def shadow_image(img):
    alpha = img.getchannel("A").point(lambda v: int(v * SHADOW_STRENGTH))
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    out.putalpha(alpha)
    return out


def layers_of(pack, name, meta, row, column):
    """(image, centre offset) of every layer of one frame, in draw order."""
    out = [(shadow_image, pack.frame(meta, "shadow", row, column))]
    out.append((None, pack.frame(meta, "base", row, column)))
    if "mask" in meta["layers"]:
        out.append((lambda img: tinted(img, PLAYER_COLOR), pack.frame(meta, "mask", row, column)))
    return out


def row_label(name, row, directions):
    if directions == 18:
        gun, move = GUN_ROWS[row]
        return f"{row + 1}: gun {DIRECTION_NAMES[gun]} run {DIRECTION_NAMES[move]}"
    if directions == 8:
        return f"{row + 1}: {DIRECTION_NAMES[row]}"
    return name


def render_animation(pack, name, zoom, columns):
    meta = pack.data["animations"][name]
    dirs, frames = meta["direction_count"], meta["frame_count"]
    # extent of every layer of every frame around the entity position
    left = top = math.inf
    right = bottom = -math.inf
    for key, info in meta["layers"].items():
        sx, sy = info["shift"][0] * pack.ppt, info["shift"][1] * pack.ppt
        w, h = info["width"] * pack.zoom(meta), info["height"] * pack.zoom(meta)
        left = min(left, sx - w / 2)
        right = max(right, sx + w / 2)
        top = min(top, sy - h / 2)
        bottom = max(bottom, sy + h / 2)
    cell_w, cell_h = int(math.ceil(right - left)) + 4, int(math.ceil(bottom - top)) + 4
    anchor = (2 - left, 2 - top)
    cols = min(columns, dirs)
    rows = int(math.ceil(dirs / cols))
    images = []
    for f in range(frames):
        canvas = Image.new("RGBA", (cols * cell_w, rows * (cell_h + LABEL_HEIGHT)), GROUND)
        draw = ImageDraw.Draw(canvas)
        for d in range(dirs):
            ox = (d % cols) * cell_w
            oy = (d // cols) * (cell_h + LABEL_HEIGHT) + LABEL_HEIGHT
            draw.text((ox + 2, oy - LABEL_HEIGHT + 1), row_label(name, d, dirs), fill=(255, 255, 255, 255))
            ax, ay = ox + anchor[0], oy + anchor[1]
            for effect, (img, (cx, cy)) in layers_of(pack, name, meta, d, f):
                if effect is not None:
                    img = effect(img)
                x = int(round(ax + cx - img.width / 2))
                y = int(round(ay + cy - img.height / 2))
                layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
                layer.paste(img, (x, y))
                canvas = Image.alpha_composite(canvas, layer)
            # the entity position, on top so the sprite does not hide it
            draw = ImageDraw.Draw(canvas)
            draw.line([(ax - 3, ay), (ax + 3, ay)], fill=(255, 255, 0, 255))
            draw.line([(ax, ay - 3), (ax, ay + 3)], fill=(255, 255, 0, 255))
        if zoom != 1:
            canvas = canvas.resize((canvas.width * zoom, canvas.height * zoom), Image.NEAREST)
        images.append(canvas.convert("RGB"))
    # Factorio advances animation_speed frames per tick, 60 ticks per second
    duration = max(20, int(round(1000 / (60 * pack.speeds.get(name, 1)))))
    return images, duration


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("pack", help="avatar pack folder or zip")
    parser.add_argument("--out", default="build/preview", help="folder for the GIFs (default: build/preview)")
    parser.add_argument("--animation", action="append", help="only this animation (may be repeated)")
    parser.add_argument("--zoom", type=int, default=1, help="scale the GIFs up by this whole factor")
    parser.add_argument("--columns", type=int, default=8, help="directions per row (default 8)")
    args = parser.parse_args(argv)

    pack = Pack(args.pack)
    names = args.animation or list(pack.data["animations"])
    unknown = [n for n in names if n not in pack.data["animations"]]
    if unknown:
        parser.error(f"the pack has no animation {', '.join(unknown)}")
    os.makedirs(args.out, exist_ok=True)
    for name in names:
        images, duration = render_animation(pack, name, args.zoom, args.columns)
        path = os.path.join(args.out, f"{pack.data['id']}-{name}.gif")
        images[0].save(path, save_all=True, append_images=images[1:], duration=duration, loop=0, optimize=False)
        print(f"wrote {path} ({len(images)} frames, {duration} ms each)")


if __name__ == "__main__":
    main()
