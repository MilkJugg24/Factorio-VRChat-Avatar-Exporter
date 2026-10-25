"""Unit tests for the Python tools: tools/lua_table.py and the avatar pack checker
(tools/avatar_pack/check.py). No Factorio needed; Pillow is.

    python3 -m unittest discover -s tests -p "test_*.py"
"""

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.insert(0, os.path.join(REPO, "tools"))

import lua_table  # noqa: E402
from avatar_pack import check  # noqa: E402

MAKE_PACK = os.path.join(REPO, "tools", "avatar_pack", "make_test_pack.py")


class LuaTableTests(unittest.TestCase):
    def test_values(self):
        self.assertEqual(lua_table.loads("return { a = 1, b = -2.5e3, c = true, d = false, e = nil }"),
                         {"a": 1, "b": -2500.0, "c": True, "d": False})
        self.assertEqual(lua_table.loads("{ 1, 2, 3, }"), [1, 2, 3])
        self.assertEqual(lua_table.loads("{ x = 0x1F; y = .5 }"), {"x": 31, "y": 0.5})

    def test_strings_are_lua_5_2_bytes(self):
        self.assertEqual(lua_table.loads(r'{ "\231\171\156", "\xe7\xab\x9c", "竜" }'), ["竜", "竜", "竜"])
        self.assertEqual(lua_table.loads(r'{ "a\z   b", "q\"\\" }'), ["ab", 'q"\\'])
        self.assertEqual(lua_table.loads('{ [[\nlong]], [==[a]]b]==] }'), ["long", "a]]b"])
        for bad in (r'{ "\u{41}" }', r'{ "\300" }', r'{ "\xff" }', r'{ "\q" }', '{ "a\n" }'):
            with self.subTest(bad=bad), self.assertRaises(lua_table.LuaSyntaxError):
                lua_table.loads(bad)

    def test_comments(self):
        self.assertEqual(lua_table.loads("-- c\n--[[ block\n]] return { x = 1 } -- end"), {"x": 1})

    def test_tables(self):
        self.assertEqual(lua_table.loads("{}"), [])
        self.assertEqual(lua_table.loads("{ 1, nil, 3 }"), {1: 1, 3: 3})
        self.assertEqual(lua_table.loads("{ 1, 2, nil }"), [1, 2])
        self.assertEqual(lua_table.loads('{ [1.0] = "a" }'), {1: "a"})
        with self.assertRaises(lua_table.LuaSyntaxError):
            lua_table.loads('{ [1] = "a", [true] = "b" }')  # Python would merge the keys

    def test_rejects_code(self):
        for bad in ("{ x = foo }", "os.exit()", "return { 1 } 2", "{ a == 1 }", "{ [nil] = 1 }"):
            with self.subTest(bad=bad), self.assertRaises(lua_table.LuaSyntaxError):
                lua_table.loads(bad)

    def test_round_trips(self):
        value = {"t": "竜" * 30 + '"\\\n\x01', "list": [1, 2.5, "x"], "end": 1, "with space": [{"q": 1}],
                 "long": ["__vrc-avatar-test__/graphics/fly-gun-base-%d.png" % i for i in range(3)]}
        self.assertEqual(lua_table.loads(lua_table.dumps(value)), value)


class ExporterVersionTests(unittest.TestCase):
    def test_editor_version_matches_package_json(self):
        package = os.path.join(REPO, "unity", "Packages", "com.vrcavatars.factorio-avatar-exporter")
        with open(os.path.join(package, "package.json"), encoding="utf-8") as fh:
            version = json.load(fh)["version"]
        with open(os.path.join(package, "Editor", "PackExport.cs"), encoding="utf-8") as fh:
            match = re.search(r'public const string Version = "([^"]+)";', fh.read())
        self.assertIsNotNone(match)
        self.assertEqual(match.group(1), version, "ExporterInfo.Version in Editor/PackExport.cs must match package.json")


class PackCheckTests(unittest.TestCase):
    """check.py must accept what the game accepts and report, not crash on, what it does not."""

    @classmethod
    def setUpClass(cls):
        cls.work = tempfile.mkdtemp()
        subprocess.run([sys.executable, MAKE_PACK, "--out", cls.work, "--id", "edge", "--max-sheet", "1024",
                        "--zip"], check=True, stdout=subprocess.DEVNULL)
        cls.source = os.path.join(cls.work, "vrc-avatar-edge")

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.work)

    def copy(self):
        path = os.path.join(tempfile.mkdtemp(dir=self.work), "vrc-avatar-edge")
        shutil.copytree(self.source, path)
        return path

    def edit(self, path, change):
        lua = os.path.join(path, "prototypes", "avatar-pack.lua")
        with open(lua, encoding="utf-8") as fh:
            data = lua_table.loads(fh.read())
        change(data)
        with open(lua, "w", encoding="utf-8") as fh:
            fh.write("return " + lua_table.dumps(data) + "\n")

    def errors(self, path):
        return check.check(path).errors

    def test_generated_pack_and_zip_pass(self):
        self.assertEqual(self.errors(self.source), [])
        self.assertEqual(self.errors(os.path.join(self.work, "vrc-avatar-edge_1.0.0.zip")), [])

    def test_empty_tables_are_tables(self):
        path = self.copy()
        self.edit(path, lambda d: d.update(timing={}))
        self.assertEqual(self.errors(path), [])
        self.edit(path, lambda d: d.update(timing={"animation_speed": {}}))
        self.assertEqual(self.errors(path), [])

    def test_integral_floats_are_integers(self):
        def floats(d):
            d["format_version"] = 1.0
            d["icon"]["size"] = 64.0
            for meta in d["animations"].values():
                meta["frame_count"] = float(meta["frame_count"])
                for info in meta["layers"].values():
                    info["width"] = float(info["width"])
        path = self.copy()
        self.edit(path, floats)
        self.assertEqual(self.errors(path), [])

    def test_optional_fields_can_be_left_out(self):
        path = self.copy()
        self.edit(path, lambda d: [d.pop(k) for k in ("pixels_per_tile", "tintable", "flight", "author", "timing")])
        self.assertEqual(self.errors(path), [])

    def test_last_split_file_may_be_padded(self):
        from PIL import Image
        path = self.copy()
        last = os.path.join(path, "graphics", "run-gun-base-3.png")
        img = Image.open(last)
        padded = Image.new("RGBA", (img.width, img.height * 4))  # 2 rows padded to lines_per_file = 8
        padded.paste(img, (0, 0))
        padded.save(last)
        self.assertEqual(self.errors(path), [])

    def test_malformed_icon_is_reported(self):
        path = self.copy()
        self.edit(path, lambda d: d.update(icon="__vrc-avatar-edge__/graphics/icon.png"))
        self.assertIn("needs an icon with a filename and a size", self.errors(path))

    def test_unreadable_png_is_reported(self):
        path = self.copy()
        with open(os.path.join(path, "graphics", "idle-base.png"), "wb") as fh:
            fh.write(b"not a png")
        with open(os.path.join(path, "thumbnail.png"), "wb") as fh:
            fh.write(b"")
        errors = self.errors(path)
        self.assertTrue(any("idle-base.png is not a readable PNG" in e for e in errors), errors)
        self.assertTrue(any("thumbnail.png is not a readable PNG" in e for e in errors), errors)

    def test_zip_with_two_folders_is_reported(self):
        bad = os.path.join(self.work, "two-folders.zip")
        with zipfile.ZipFile(bad, "w") as zf:
            zf.writestr("a/info.json", "{}")
            zf.writestr("b/info.json", "{}")
        self.assertTrue(any("exactly one folder" in e for e in self.errors(bad)))


if __name__ == "__main__":
    unittest.main()
