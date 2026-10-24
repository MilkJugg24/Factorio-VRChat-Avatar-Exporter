#!/usr/bin/env python3
"""Builds the avatar exporter as a .unitypackage, to import with Assets > Import Package > Custom Package.

    python3 tools/make_unitypackage.py [--out build]

Writes build/FactorioAvatarExporter_<version>.unitypackage, version from the package's package.json.
Unity imports .unitypackage files into Assets/, so the files land in Assets/FactorioAvatarExporter/.

A .unitypackage is a gzipped tar with one folder per asset, named by the asset's GUID, holding `asset`
(the file; folders have none), `asset.meta` and `pathname` (the path in the project). GUIDs come from
the file's path in the package, so they stay the same from one build to the next and an import updates
an earlier one in place. The archive is byte-identical for the same sources.
"""

import argparse
import gzip
import hashlib
import io
import json
import os
import tarfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACKAGE = os.path.join(REPO, "unity", "Packages", "com.milkjugg24.factorio-avatar-exporter")
PACKAGE_NAME = "com.milkjugg24.factorio-avatar-exporter"
ASSET_ROOT = "Assets/FactorioAvatarExporter"
SKIP_DIRS = {"bin", "obj", "ERROR_LOGS"}  # ERROR_LOGS: the exporter's own logs, when used from this folder
SKIP_FILES = {"package.json"}  # only meaningful to the Package Manager; ignored in Assets

IMPORTERS = {
    ".cs": "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n"
           "  executionOrder: 0\n  icon: {instanceID: 0}\n",
    ".asmdef": "AssemblyDefinitionImporter:\n  externalObjects: {}\n",
    ".md": "TextScriptImporter:\n  externalObjects: {}\n",
}
DEFAULT_IMPORTER = "DefaultImporter:\n  externalObjects: {}\n"
IMPORTER_TAIL = "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"


def guid(relative):
    return hashlib.md5(f"{PACKAGE_NAME}/{relative}".encode("utf-8")).hexdigest()


def meta(relative, is_dir):
    lines = f"fileFormatVersion: 2\nguid: {guid(relative)}\n"
    if is_dir:
        return lines + "folderAsset: yes\n" + DEFAULT_IMPORTER + IMPORTER_TAIL
    importer = IMPORTERS.get(os.path.splitext(relative)[1].lower(), DEFAULT_IMPORTER)
    return lines + importer + IMPORTER_TAIL


def assets():
    """(path relative to the package, is_dir) for every folder and file, sorted, '' for the root."""
    found = [("", True)]
    for root, dirs, files in os.walk(PACKAGE):
        dirs[:] = sorted(d for d in dirs if d not in SKIP_DIRS and not d.startswith("."))
        rel_root = os.path.relpath(root, PACKAGE).replace(os.sep, "/")
        rel_root = "" if rel_root == "." else rel_root
        for d in dirs:
            found.append((f"{rel_root}/{d}".lstrip("/"), True))
        for f in sorted(files):
            if f.endswith(".meta") or f.startswith(".") or (rel_root == "" and f in SKIP_FILES):
                continue
            found.append((f"{rel_root}/{f}".lstrip("/"), False))
    return sorted(found)


def add(tar, name, data):
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mtime = 0
    info.mode = 0o644
    tar.addfile(info, io.BytesIO(data))


def build(out_dir):
    with open(os.path.join(PACKAGE, "package.json"), encoding="utf-8") as f:
        version = json.load(f)["version"]
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, f"FactorioAvatarExporter_{version}.unitypackage")
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w", format=tarfile.USTAR_FORMAT) as tar:
        for relative, is_dir in assets():
            folder = guid(relative)
            pathname = f"{ASSET_ROOT}/{relative}".rstrip("/")
            if not is_dir:
                with open(os.path.join(PACKAGE, relative), "rb") as f:
                    add(tar, f"{folder}/asset", f.read())
            add(tar, f"{folder}/asset.meta", meta(relative, is_dir).encode("utf-8"))
            add(tar, f"{folder}/pathname", pathname.encode("utf-8"))
    with open(path, "wb") as f:
        with gzip.GzipFile(fileobj=f, mode="wb", mtime=0, filename="") as gz:
            gz.write(raw.getvalue())
    return path, version


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", default=os.path.join(REPO, "build"), help="output folder (default build/)")
    args = parser.parse_args()
    path, _ = build(args.out)
    print(path)


if __name__ == "__main__":
    main()
