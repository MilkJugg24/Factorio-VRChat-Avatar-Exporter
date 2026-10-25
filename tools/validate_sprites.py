"""Checks avatar packs (docs/vrc-avatar-format.md), folders or mod zips: everything
vrc-avatars checks when the game loads, plus that every sprite sheet exists and has
exactly the size Factorio will expect from the pack's metadata.

The headless server does not load graphics, so it can not catch those mistakes.

    python3 tools/validate_sprites.py --pack path/to/vrc-avatar-mika_1.0.0.zip [more packs...]
"""

import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import avatar_pack.check as pack_check  # noqa: E402


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--pack", nargs="+", metavar="PATH", required=True,
                        help="avatar pack folders or zips to check")
    args = parser.parse_args(argv)
    failed = 0
    for path in args.pack:
        failed += 0 if pack_check.report(path) else 1
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
