#!/usr/bin/env python3
"""Generate the standalone kOSImproved .ckan for a release ZIP.

Fills the fields that NetKAN's GitHub and KSP-AVC transformers would add to
ckan/kOSImproved.netkan: version and KSP range from the packaged kOS.version,
the release download URL, sizes and hashes. Only the standard library is used.
"""

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import zipfile


ROOT = Path(__file__).resolve().parent.parent
NETKAN = ROOT / "ckan/kOSImproved.netkan"
KREF = re.compile(r"^#/ckan/github/(?P<repository>[^/]+/[^/]+)/asset_match/(?P<asset>.+)$")


def load_netkan(path=NETKAN):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def asset_pattern(netkan):
    match = KREF.match(netkan.get("$kref", ""))
    if not match:
        raise ValueError("$kref must be a GitHub reference with an asset_match filter")
    return match["repository"], re.compile(match["asset"])


def ksp_range(metadata):
    low = metadata.get("KSP_VERSION_MIN") or metadata.get("KSP_VERSION")
    high = metadata.get("KSP_VERSION_MAX") or metadata.get("KSP_VERSION")
    if not low or not high:
        raise ValueError("kOS.version has no KSP version range")
    return {"ksp_version": low} if low == high else {"ksp_version_min": low, "ksp_version_max": high}


def inflate(netkan, zip_path, download_url=None, release_date=None, test=False):
    zip_path = Path(zip_path)
    repository, pattern = asset_pattern(netkan)
    if not pattern.search(zip_path.name) and not test:
        raise ValueError(f"{zip_path.name} does not match the release asset filter {pattern.pattern}")
    if len(netkan.get("install", [])) != 1 or "file" not in netkan["install"][0]:
        raise ValueError("Expected one install stanza with a literal file path")
    prefix = netkan["install"][0]["file"].rstrip("/") + "/"

    with zipfile.ZipFile(zip_path) as archive:
        members = [info for info in archive.infolist() if not info.is_dir()]
        outside = [info.filename for info in members if not info.filename.startswith(prefix)]
        if outside:
            raise ValueError(f"Archive entries outside {prefix} would not be installed: {outside[:3]}")
        version_files = [info.filename for info in members if info.filename.endswith(".version")]
        if version_files != [prefix + "kOS.version"]:
            raise ValueError(f"Expected exactly one installed {prefix}kOS.version, found {version_files}")
        metadata = json.loads(archive.read(version_files[0]).decode("utf-8-sig"))
        install_size = sum(info.file_size for info in members)

    version = metadata.get("VERSION")
    if not version or not re.fullmatch(r"[0-9]+(\.[0-9]+){3}", version):
        raise ValueError(f"Invalid packaged version {version!r}")
    if not test and zip_path.name != f"kOS-v{version}.zip":
        raise ValueError(f"{zip_path.name} does not match packaged version {version}")
    data = zip_path.read_bytes()

    module = {"spec_version": 1}
    for key, value in netkan.items():
        if not key.startswith(("$", "x_netkan")):
            module[key] = value
    module["version"] = version
    module.update(ksp_range(metadata))
    module["download"] = download_url or \
        f"https://github.com/{repository}/releases/download/v{version}/{zip_path.name}"
    module["download_size"] = len(data)
    module["download_hash"] = {
        "sha1": hashlib.sha1(data).hexdigest().upper(),
        "sha256": hashlib.sha256(data).hexdigest().upper(),
    }
    module["download_content_type"] = "application/zip"
    module["install_size"] = install_size
    if release_date:
        module["release_date"] = release_date
    module["x_generated_by"] = "kOSIm .ci/ckan.py"
    return module


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("zip", type=Path, help="release ZIP from .ci/package.py")
    parser.add_argument("--output", type=Path, help="default: artifacts/kOSImproved-<version>.ckan")
    parser.add_argument("--release-date", help="ISO 8601 publication time of the GitHub release")
    parser.add_argument("--download-url", help="override the GitHub release URL (local tests only)")
    parser.add_argument("--test", action="store_true",
                        help="accept test package names such as -dirty ZIPs (never publish the result)")
    args = parser.parse_args()
    if args.download_url and not args.test:
        parser.error("--download-url is for local tests; also pass --test")
    try:
        module = inflate(load_netkan(), args.zip, args.download_url, args.release_date, args.test)
        output = args.output or ROOT / "artifacts" / f"kOSImproved-{module['version']}.ckan"
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(module, indent=4) + "\n", encoding="utf-8")
    except (OSError, ValueError, zipfile.BadZipFile) as error:
        print(f"CKAN metadata failed: {error}", file=sys.stderr)
        return 1
    print(output)
    return 0


if __name__ == "__main__":
    sys.exit(main())
