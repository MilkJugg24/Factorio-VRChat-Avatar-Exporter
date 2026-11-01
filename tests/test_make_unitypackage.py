"""Tests for tools/make_unitypackage.py."""

import importlib.util
import os
import unittest

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPEC = importlib.util.spec_from_file_location("make_unitypackage", os.path.join(REPO, "tools", "make_unitypackage.py"))
make_unitypackage = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(make_unitypackage)


class StableMtimeTest(unittest.TestCase):
    def test_same_contents_same_time(self):
        self.assertEqual(make_unitypackage.stable_mtime(b"a"), make_unitypackage.stable_mtime(b"a"))

    def test_changed_contents_of_the_same_size_change_the_time(self):
        self.assertNotEqual(make_unitypackage.stable_mtime(b"a"), make_unitypackage.stable_mtime(b"b"))

    def test_time_is_not_the_epoch(self):
        self.assertGreater(make_unitypackage.stable_mtime(b""), 0)


if __name__ == "__main__":
    unittest.main()
