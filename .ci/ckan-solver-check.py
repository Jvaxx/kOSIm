#!/usr/bin/env python3
"""Exercise a kOSImproved package with the real CKAN client on fake game instances.

Manual pre-release check, not part of .ci/test.sh: it needs the `ckan` command and
network access to CKAN-meta and the dependencies' hosts (ModuleManager, MechJeb2,
kOS-MechJeb2, upstream kOS). The package and a second, test-only version are
served from loopback through a local CKAN repository. CKAN runs with HOME and
XDG directories inside the work directory, so the user's CKAN configuration,
instances and download cache are not used; the script verifies that the real
CKAN config file is unchanged. No real game folder is touched.
"""

import argparse
import functools
import hashlib
import http.server
import importlib.util
import io
import json
import os
from pathlib import Path
import shlex
import subprocess
import sys
import tarfile
import threading
import time
import zipfile


ROOT = Path(__file__).resolve().parent.parent
GAME_VERSION = "1.12.5"
ADDON = "kOS-MechJeb2"  # depends on the kOS identifier, which kOSImproved provides
USER_FILE = "kOS/Plugins/PluginData/kOS/solver-check.txt"

spec = importlib.util.spec_from_file_location("ckan_metadata", Path(__file__).with_name("ckan.py"))
ckan_metadata = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ckan_metadata)


class CheckFailed(Exception):
    pass


def next_version(version):
    parts = version.split(".")
    parts[-1] = str(int(parts[-1]) + 1)
    return ".".join(parts)


def bumped_package(source, target, version):
    """Copy a package with a newer kOS.version, as a stand-in for the next release."""
    with zipfile.ZipFile(source) as old, zipfile.ZipFile(target, "x", zipfile.ZIP_DEFLATED) as new:
        for info in old.infolist():
            data = old.read(info)
            if info.filename == "GameData/kOS/kOS.version":
                metadata = json.loads(data.decode("utf-8-sig"))
                metadata["VERSION"] = version
                data = json.dumps(metadata, indent=2).encode()
            elif info.filename == "GameData/kOS/SOURCE.txt":
                data = b"CKAN solver-check copy with a rewritten version. Do not distribute.\n\n" + data
            new.writestr(info, data)


class Server:
    def __init__(self, directory):
        handler = functools.partial(QuietHandler, directory=str(directory))
        self.httpd = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler)
        self.url = f"http://127.0.0.1:{self.httpd.server_address[1]}"
        threading.Thread(target=self.httpd.serve_forever, daemon=True).start()

    def close(self):
        self.httpd.shutdown()
        self.httpd.server_close()


class QuietHandler(http.server.SimpleHTTPRequestHandler):
    def log_message(self, format, *args):
        pass


class Ckan:
    def __init__(self, command, work, log):
        self.command = command
        self.log = log
        self.env = dict(os.environ)
        for name in ("HOME", "XDG_DATA_HOME", "XDG_CONFIG_HOME", "XDG_CACHE_HOME"):
            path = work / "env" / name.lower()
            path.mkdir(parents=True, exist_ok=True)
            self.env[name] = str(path)

    def __call__(self, *args, stdin=None, expect_ok=True):
        command = self.command + list(args)
        result = subprocess.run(command, env=self.env, input=stdin, capture_output=True,
                                text=True, timeout=900)
        output = result.stdout + result.stderr
        self.log.write(f"$ {shlex.join(command)}  [exit {result.returncode}]\n{output}\n")
        self.log.flush()
        if expect_ok and result.returncode != 0:
            raise CheckFailed(f"ckan {' '.join(args)} exited {result.returncode}")
        return output

    def installed(self, instance):
        modules = {}
        for line in self("list", "--porcelain", "--instance", instance).splitlines():
            fields = line.split()
            if len(fields) == 3 and fields[0] in "-+^!?~":
                modules[fields[1]] = fields[2]
        return modules


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def run_checks(ckan, work, server, v1, v2, v1_dll):
    results = []

    def check(description, condition, detail=""):
        results.append(description)
        print(f"  ok  {description}" if condition else f"  FAIL {description} {detail}")
        if not condition:
            raise CheckFailed(f"{description} {detail}".strip())

    def instance(name):
        path = work / "instances" / name
        ckan("instance", "fake", "--headless", name, str(path), GAME_VERSION)
        ckan("repo", "add", "--instance", name, "kosim-test", f"{server.url}/repo.tar.gz")
        ckan("update", "--headless", "--instance", name)
        return path / "GameData"

    print("Fresh install, add-on, upgrade and removal:")
    game = instance("fresh")
    ckan("install", "--headless", "--instance", "fresh", f"kOSImproved={v1}")
    mods = ckan.installed("fresh")
    check("installs kOSImproved with ModuleManager",
          mods.get("kOSImproved") == v1 and "ModuleManager" in mods, mods)
    check("installs the packaged kOS.dll", sha256(game / "kOS/Plugins/kOS.dll") == v1_dll)
    check("installs license and source directions",
          all((game / "kOS" / name).is_file() for name in
              ("LICENSE.md", "THIRD_PARTY_NOTICES.md", "SOURCE.txt",
               "Licenses/SharpZipLib-0.86.0.518-COPYING.txt")))

    ckan("install", "--headless", "--no-recommends", "--instance", "fresh", ADDON)
    mods = ckan.installed("fresh")
    check(f"{ADDON} uses kOSImproved as its kOS", ADDON in mods and "kOS" not in mods, mods)

    output = ckan("install", "--headless", "--instance", "fresh", "kOS", expect_ok=False)
    check("does not add upstream kOS beside the fork",
          "kOS" not in ckan.installed("fresh") and "Nothing to install" in output)

    (game / USER_FILE).parent.mkdir(parents=True, exist_ok=True)
    (game / USER_FILE).write_text("user data\n")
    ckan("upgrade", "--headless", "--instance", "fresh", "kOSImproved")
    version_file = json.loads((game / "kOS/kOS.version").read_text(encoding="utf-8-sig"))
    check("upgrades to the next version", ckan.installed("fresh").get("kOSImproved") == v2
          and version_file["VERSION"] == v2)
    check("upgrade keeps PluginData", (game / USER_FILE).read_text() == "user data\n")

    ckan("remove", "--instance", "fresh", "kOSImproved", stdin="y\n")
    mods = ckan.installed("fresh")
    remaining = sorted(p.relative_to(game).as_posix() for p in (game / "kOS").rglob("*") if p.is_file())
    check("removal also removes the dependent add-on",
          "kOSImproved" not in mods and ADDON not in mods, mods)
    check("removal leaves only user PluginData in GameData/kOS", remaining == [USER_FILE], remaining)

    output = ckan("install", "--no-recommends", "--instance", "fresh", ADDON, stdin="2\ny\n")
    mods = ckan.installed("fresh")
    check("asks which kOS provider an add-on should use",
          "kOSImproved (kOS Improved)" in output and "kOS (kOS: Scriptable Autopilot System)" in output)
    check("choosing kOSImproved installs the add-on without upstream kOS",
          mods.get("kOSImproved") == v2 and ADDON in mods and "kOS" not in mods, mods)

    print("Replacing CKAN-installed upstream kOS:")
    game = instance("upstream")
    ckan("install", "--headless", "--no-recommends", "--instance", "upstream", "kOS", ADDON)
    mods = ckan.installed("upstream")
    check("upstream kOS and add-on installed", "kOS" in mods and ADDON in mods, mods)
    (game / USER_FILE).parent.mkdir(parents=True, exist_ok=True)
    (game / USER_FILE).write_text("user data\n")
    upstream_dll = sha256(game / "kOS/Plugins/kOS.dll")

    output = ckan("install", "--instance", "upstream", f"kOSImproved={v1}", stdin="n\n", expect_ok=False)
    check("refuses the fork while upstream kOS is installed",
          "conflicts with kOS" in output and "kOSImproved" not in ckan.installed("upstream")
          and sha256(game / "kOS/Plugins/kOS.dll") == upstream_dll)

    ckan("remove", "--instance", "upstream", "kOS", stdin="y\n")
    check("removing upstream kOS keeps PluginData", (game / USER_FILE).read_text() == "user data\n")
    ckan("install", "--instance", "upstream", f"kOSImproved={v1}", ADDON, stdin="y\n")
    mods = ckan.installed("upstream")
    check("fork and add-on install after removing upstream",
          mods.get("kOSImproved") == v1 and ADDON in mods and "kOS" not in mods, mods)
    check("replacement installs the fork DLL and keeps PluginData",
          sha256(game / "kOS/Plugins/kOS.dll") == v1_dll
          and (game / USER_FILE).read_text() == "user data\n")

    print("Standalone .ckan files (before the CKAN index lists the fork):")
    path = work / "instances" / "standalone"
    ckan("instance", "fake", "--headless", "standalone", str(path), GAME_VERSION)
    ckan("update", "--headless", "--instance", "standalone")
    game = path / "GameData"
    v1_ckan, v2_ckan = (str(work / "srv/meta" / f"kOSImproved-{v}.ckan") for v in (v1, v2))
    ckan("install", "--instance", "standalone", "-c", v1_ckan, stdin="y\n")
    check("installs from a .ckan file", ckan.installed("standalone").get("kOSImproved") == v1)
    (game / USER_FILE).parent.mkdir(parents=True, exist_ok=True)
    (game / USER_FILE).write_text("user data\n")
    ckan("install", "--instance", "standalone", "-c", v2_ckan, stdin="y\n")
    ckan("upgrade", "--instance", "standalone", "-c", v2_ckan, stdin="y\n", expect_ok=False)
    check("a newer .ckan does not upgrade in place (documented limitation)",
          ckan.installed("standalone").get("kOSImproved") == v1)
    ckan("remove", "--instance", "standalone", "kOSImproved", stdin="y\n")
    ckan("install", "--instance", "standalone", "-c", v2_ckan, stdin="y\n")
    check("remove and install the newer .ckan updates and keeps PluginData",
          ckan.installed("standalone").get("kOSImproved") == v2
          and (game / USER_FILE).read_text() == "user data\n")
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("zip", type=Path, help="package from .ci/package.py")
    parser.add_argument("--work", type=Path,
                        default=ROOT / "artifacts/tests/ckan-solver" / time.strftime("%Y%m%d-%H%M%S"))
    parser.add_argument("--ckan", default="ckan", help="CKAN command line, e.g. 'mono ckan.exe'")
    parser.add_argument("--test", action="store_true", help="accept a -dirty test package")
    args = parser.parse_args()

    real_config = Path.home() / ".local/share/CKAN/config.json"
    real_before = sha256(real_config) if real_config.exists() else None
    if args.work.exists() and any(args.work.iterdir()):
        parser.error(f"{args.work} is not empty")
    srv = args.work / "srv"
    (srv / "meta").mkdir(parents=True)
    server = Server(srv)
    log = (args.work / "ckan.log").open("w", encoding="utf-8")
    try:
        netkan = ckan_metadata.load_netkan()
        v1_zip = srv / args.zip.name
        v1_zip.write_bytes(args.zip.read_bytes())
        v1 = ckan_metadata.inflate(netkan, v1_zip, test=True)["version"]
        v2 = next_version(v1)
        v2_zip = srv / f"kOS-v{v2}-solvertest.zip"
        bumped_package(v1_zip, v2_zip, v2)
        repo = io.BytesIO()
        with tarfile.open(fileobj=repo, mode="w:gz") as tar:
            for path in (v1_zip, v2_zip):
                module = ckan_metadata.inflate(netkan, path, f"{server.url}/{path.name}", test=args.test or path == v2_zip)
                data = json.dumps(module, indent=4).encode()
                (srv / "meta" / f"kOSImproved-{module['version']}.ckan").write_bytes(data)
                info = tarfile.TarInfo(f"meta/kOSImproved-{module['version']}.ckan")
                info.size = len(data)
                tar.addfile(info, io.BytesIO(data))
        (srv / "repo.tar.gz").write_bytes(repo.getvalue())
        with zipfile.ZipFile(v1_zip) as archive:
            v1_dll = hashlib.sha256(archive.read("GameData/kOS/Plugins/kOS.dll")).hexdigest()

        ckan = Ckan(shlex.split(args.ckan), args.work, log)
        print(ckan("version").strip().splitlines()[-1])
        print(f"Package {args.zip.name} ({v1}); test upgrade {v2}; work {args.work}")
        results = run_checks(ckan, args.work, server, v1, v2, v1_dll)
    except (CheckFailed, OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"CKAN solver check failed: {error}\nLog: {args.work / 'ckan.log'}", file=sys.stderr)
        return 1
    finally:
        log.close()
        server.close()
        real_after = sha256(real_config) if real_config.exists() else None
        if real_after != real_before:
            print(f"WARNING: {real_config} changed during the check", file=sys.stderr)
    if real_after != real_before:
        return 1
    print(f"All {len(results)} CKAN checks passed. Log: {args.work / 'ckan.log'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
