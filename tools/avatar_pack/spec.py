"""Constants of the avatar pack format (docs/vrc-avatar-format.md).

Keep in sync with vrc-avatars/scripts/pack-format.lua; tests/run_headless.sh --test-packs
and --broken-packs check that both accept the same packs and reject the same mistakes.
"""

import math
import re

MAIN_MOD = "vrc-avatars"
FACTORIO_VERSION = "2.1"  # info.json factorio_version: Factorio only loads mods made for its own minor version
DATA_TYPE = "vrc-avatars.avatar-pack"
SUPPORTED_VERSION = 1
MOD_PREFIX = "vrc-avatar-"
ID_PATTERN = re.compile(r"[a-z0-9][a-z0-9-]*")
MAX_ID_LENGTH = 35
MAX_TITLE_LENGTH = 80
MAX_SHEET_SIZE = 4096
MAX_FRAMES = 255  # Factorio's limit for one animation
MIN_MAIN_VERSION = (0, 1, 0)
# Avatar bundles: several packs in one mod (docs/vrc-avatar-format.md, "Bundles")
BUNDLE_PREFIX = "avatar-bundle-"
MIN_BUNDLE_MAIN_VERSION = (0, 3, 0)
# Factorio 2.0 aborts on start, without a message, when a mod's info.json title is longer
INFO_TITLE_MAX_BYTES = 100

DIRECTIONS = {
    "idle": 8, "idle-gun": 8, "run": 8, "run-gun": 18, "mine": 8, "corpse": 1,
    "hover": 8, "hover-gun": 8, "fly": 8, "fly-gun": 18, "take-off": 8,
}
REQUIRED = ("idle", "idle-gun", "run", "run-gun", "mine", "corpse")
FLIGHT = ("hover", "hover-gun", "fly", "fly-gun", "take-off")
OPTIONAL_LAYERS = {"run-gun": ("shadow-flipped",)}

DEFAULT_DISTANCE_PER_FRAME = 0.238


def default_timing(run_frames, mine_frames):
    """Frame numbers a pack gets when its timing leaves them out (same as format.default_timing)."""
    return {
        "mine_impact_frame": max(1, math.floor(mine_frames * 9 / 12 + 0.5)),
        "left_foot_frames": [1],
        "right_foot_frames": [run_frames // 2 + 1],
    }


DATA_LUA = '''local pack = require("prototypes.avatar-pack")
data:extend({
  {
    type = "mod-data",
    name = "vrc-avatar-" .. pack.id,
    data_type = "vrc-avatars.avatar-pack",
    data = pack,
  },
})
'''


def bundle_data_lua(bundle_id, ids):
    """The data.lua of a bundle, byte for byte (PackBundler.DataLua in the exporter's Core)."""
    listed = ", ".join(f'"{i}"' for i in sorted(ids))
    return ('-- An avatar bundle: several avatar packs in one mod (docs/vrc-avatar-format.md, "Bundles").\n'
            f'local bundle = "{BUNDLE_PREFIX}{bundle_id}"\n'
            f'for _, id in ipairs({{ {listed} }}) do\n'
            '  local pack = require("characters." .. id .. ".avatar-pack")\n'
            '  pack.mod = bundle\n'
            '  data:extend({\n'
            '    {\n'
            '      type = "mod-data",\n'
            f'      name = "{MOD_PREFIX}" .. id,\n'
            f'      data_type = "{DATA_TYPE}",\n'
            '      data = pack,\n'
            '    },\n'
            '  })\n'
            'end\n')


def _cut(text, max_bytes):
    return text.encode("utf-8")[:max_bytes].decode("utf-8", "ignore")


def info_title(title, author=None):
    """The info.json title for a pack: "VRC Avatar: <title> by <author>", the title shortened (the author
    kept) to Factorio's limit if needed, as the exporter's PackNaming.InfoTitle does."""
    prefix, ellipsis = "VRC Avatar: ", "\u2026"
    by = f" by {author.strip()}" if author and author.strip() else ""
    text = prefix + title + by
    size = lambda s: len(s.encode("utf-8"))  # noqa: E731
    if size(text) <= INFO_TITLE_MAX_BYTES:
        return text
    room = INFO_TITLE_MAX_BYTES - size(prefix + ellipsis + by)
    if room >= 8:
        return prefix + _cut(title, room) + ellipsis + by
    return _cut(text, INFO_TITLE_MAX_BYTES - size(ellipsis)) + ellipsis


def valid_id(pack_id):
    return (isinstance(pack_id, str) and 1 <= len(pack_id) <= MAX_ID_LENGTH
            and ID_PATTERN.fullmatch(pack_id) is not None and not pack_id.startswith("corpse-"))
