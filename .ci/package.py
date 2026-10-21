#!/usr/bin/env python3
"""Build the kOSIM release ZIP from a full Release build of a committed tree.

The archive holds tracked GameData/kOS files, the built mod DLLs and kOS.version,
the GPLv3 text, third-party notices and generated source directions. Entries are
sorted with fixed metadata, so one commit and build give the same archive.
An existing archive is never updated in place.
"""

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parent.parent
MOD = "GameData/kOS"
REPOSITORY = "https://github.com/Jvaxx/kOSIm"
BUILT = ("Plugins/kOS.dll", "Plugins/kOS.Safe.dll", "kOS.version")
# Never packaged: per-install user data and KSP's generated part thumbnails.
EXCLUDED = ("Plugins/PluginData/", "Parts/@thumbs/")
SHARPZIPLIB = {
    "path": "Plugins/ICSharpCode.SharpZipLib.dll",
    "sha256": "40b3d590f95191f3e33e5d00e534fa40f823d9b1bb2a9afe05f139c4e0a3af8d",
    "license": "Licenses/SharpZipLib-0.86.0.518-COPYING.txt",
    "commit": "cfc69a68fefbc5858fe70b35f7b69fc505b8c2d6",
    "source_asset": "SharpZipLib-0.86.0.518-source.zip",
}


def load_version_checker():
    spec = importlib.util.spec_from_file_location("check_version", Path(__file__).with_name("check-version.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def git(root, *args):
    return subprocess.run(["git", *args], cwd=root, check=True, capture_output=True, text=True).stdout


def source_directions(version, commit, dirty):
    if dirty:
        built_from = (f"  UNCOMMITTED WORKING TREE based on commit {commit}.\n"
                      "  This is a local test package. Do not distribute it.\n")
    else:
        built_from = (f"  tag:    v{version}\n"
                      f"  commit: {commit}\n"
                      f"  {REPOSITORY}/tree/{commit}\n")
    return f"""kOS Improved {version}: corresponding source

This package was built from the kOS Improved source:
{built_from}
The release page offers the source archives of the tag and the source of the
bundled SharpZipLib beside this package:
  {REPOSITORY}/releases/tag/v{version}

kOS Improved is free software under the GNU General Public License v3; see
LICENSE.md. It is an independent fork of kOS (https://github.com/KSP-KOS/KOS).

Bundled third-party component: {SHARPZIPLIB["path"]}
  SharpZipLib 0.86.0.518, the stock DLL from NuGet SharpZipLib 0.86.0 (lib/20).
  GNU GPL version 2 or later with a linking exception; see
  {SHARPZIPLIB["license"]} and THIRD_PARTY_NOTICES.md.
  Source: https://github.com/icsharpcode/SharpZipLib/tree/{SHARPZIPLIB["commit"]}
  Release-page copy: {SHARPZIPLIB["source_asset"]}
"""


def collect(root, version, commit, dirty):
    """Return {archive path: bytes} after checking the build belongs to the commit."""
    mod_root = root / "Resources" / MOD
    tracked = git(root, "ls-files", "-z", "--", f"Resources/{MOD}").split("\0")
    relative = sorted(Path(path).relative_to(f"Resources/{MOD}").as_posix() for path in tracked if path)
    if any(path.startswith(EXCLUDED) for path in relative):
        raise ValueError("Tracked files under an excluded directory: " + ", ".join(EXCLUDED))
    missing = [path for path in BUILT if not (mod_root / path).is_file()]
    if missing:
        raise ValueError("Missing build outputs (run a full Release build): " + ", ".join(missing))
    if not relative or any(path in relative for path in BUILT):
        raise ValueError("Unexpected tracked file list for the mod folder")

    files = {f"{MOD}/{path}": (mod_root / path).read_bytes() for path in relative + list(BUILT)}
    marker = f"{version}+{commit}".encode()
    for dll in BUILT[:2]:
        if marker not in files[f"{MOD}/{dll}"]:
            raise ValueError(f"{dll} was not built from commit {commit} at version {version}")
    metadata = json.loads(files[f"{MOD}/kOS.version"].decode("utf-8-sig"))
    if metadata.get("VERSION") != version:
        raise ValueError("kOS.version does not match kOS.props")
    sharpziplib = files.get(f"{MOD}/{SHARPZIPLIB['path']}", b"")
    if hashlib.sha256(sharpziplib).hexdigest() != SHARPZIPLIB["sha256"]:
        raise ValueError("Bundled SharpZipLib is not the audited 0.86.0.518 DLL")
    if f"{MOD}/{SHARPZIPLIB['license']}" not in files:
        raise ValueError("Missing tracked SharpZipLib license text")

    for name in ("LICENSE.md", "THIRD_PARTY_NOTICES.md"):
        files[f"{MOD}/{name}"] = (root / name).read_bytes()
    files[f"{MOD}/SOURCE.txt"] = source_directions(version, commit, dirty).encode()
    return files


def write_zip(path, files, timestamp):
    with zipfile.ZipFile(path, "x", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name in sorted(files):
            info = zipfile.ZipInfo(name, date_time=timestamp)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            archive.writestr(info, files[name])


def package(root, output_dir, allow_dirty=False):
    load_version_checker().check_version(root, built=True)
    version = ET.parse(root / "kOS.props").getroot().findtext("./PropertyGroup/KSPBT_ModVersion").strip()
    commit = git(root, "rev-parse", "HEAD").strip()
    dirty = bool(git(root, "status", "--porcelain", "--untracked-files=normal").strip())
    if dirty and not allow_dirty:
        raise ValueError("Working tree has changes; commit them and rebuild, "
                         "or pass --allow-dirty for a local test package")
    files = collect(root, version, commit, dirty)
    seconds = int(git(root, "show", "-s", "--format=%ct", "HEAD"))
    timestamp = time.gmtime(max(seconds, 315532800))[:6]
    output_dir.mkdir(parents=True, exist_ok=True)
    path = output_dir / f"kOS-v{version}{'-dirty' if dirty else ''}.zip"
    write_zip(path, files, timestamp)
    return path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=ROOT / "artifacts")
    parser.add_argument("--allow-dirty", action="store_true",
                        help="package uncommitted changes as a clearly named local test ZIP")
    args = parser.parse_args()
    try:
        path = package(ROOT, args.output_dir, args.allow_dirty)
    except FileExistsError as error:
        print(f"Packaging failed: {error.filename} exists; use a new name, never update a ZIP",
              file=sys.stderr)
        return 1
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f"Packaging failed: {error}", file=sys.stderr)
        return 1
    with zipfile.ZipFile(path) as archive:
        count = len(archive.infolist())
    print(f"{path} ({count} files)")
    print(f"sha256 {hashlib.sha256(path.read_bytes()).hexdigest()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
