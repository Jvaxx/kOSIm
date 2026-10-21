"""Regression coverage for the kOSImproved NetKAN file and standalone .ckan generator."""

import hashlib
import importlib.util
import json
from pathlib import Path
import re
import tempfile
import unittest
import zipfile


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


ckan = load("ckan_metadata", "ckan.py")
package = load("package", "package.py")
solver = load("ckan_solver_check", "ckan-solver-check.py")
ROOT = Path(__file__).resolve().parent.parent


class NetkanTest(unittest.TestCase):
    def setUp(self):
        self.netkan = ckan.load_netkan()

    def test_replaces_upstream_kos_under_its_own_identifier(self):
        self.assertEqual("kOSImproved", self.netkan["identifier"])
        self.assertEqual(["kOS"], self.netkan["provides"])
        self.assertEqual([{"name": "kOS"}, {"name": "kOS-Classic"}], self.netkan["conflicts"])
        self.assertEqual([{"name": "ModuleManager"}], self.netkan["depends"])
        self.assertEqual([{"file": "GameData/kOS", "install_to": "GameData"}], self.netkan["install"])
        self.assertEqual("GPL-3.0", self.netkan["license"])
        self.assertNotIn("x_netkan_epoch", self.netkan)

    def test_indexes_only_full_fork_releases_with_embedded_version_file(self):
        repository, asset = ckan.asset_pattern(self.netkan)
        self.assertEqual(package.REPOSITORY, "https://github.com/" + repository)
        self.assertEqual("#/ckan/ksp-avc", self.netkan["$vref"])
        self.assertEqual({"prereleases": False}, self.netkan["x_netkan_github"])
        self.assertEqual({"$kref", "$vref"}, {key for key in self.netkan if key.startswith("$")})
        self.assertTrue(asset.search("kOS-v1.6.0.103.zip"))
        for name in ("kOS-v1.6.0.103-dirty.zip", "kOS-v1.6.0.103.zip.sha256",
                     "kOSImproved-1.6.0.103.ckan", package.SHARPZIPLIB["source_asset"],
                     "kOS-v1.6.0.zip", "KOS-1.6.0.1.zip"):
            with self.subTest(name=name):
                self.assertFalse(asset.search(name))

    def test_version_edit_strips_the_tag_prefix(self):
        edit = re.compile(self.netkan["x_netkan_version_edit"].replace("(?<", "(?P<"))
        self.assertEqual("1.6.0.103", edit.match("v1.6.0.103")["version"])

    def test_resources_point_at_the_fork(self):
        for url in self.netkan["resources"].values():
            self.assertTrue(url.startswith(package.REPOSITORY), url)


class InflateTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.netkan = ckan.load_netkan()

    def make_zip(self, name="kOS-v2.3.4.105.zip", version_json=None, extra=()):
        path = Path(self.temp.name) / name
        metadata = {"NAME": "kOS", "VERSION": "2.3.4.105", "KSP_VERSION": "1.12",
                    "KSP_VERSION_MIN": "1.12", "KSP_VERSION_MAX": "1.12"}
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("GameData/kOS/Plugins/kOS.dll", b"x" * 100)
            archive.writestr("GameData/kOS/kOS.version", json.dumps(version_json or metadata))
            for entry in extra:
                archive.writestr(entry, b"y")
        return path

    def test_fills_release_fields_from_package(self):
        path = self.make_zip()
        module = ckan.inflate(self.netkan, path, release_date="2026-10-21T12:00:00Z")
        data = path.read_bytes()
        self.assertEqual("2.3.4.105", module["version"])
        self.assertEqual("1.12", module["ksp_version"])
        self.assertNotIn("ksp_version_min", module)
        self.assertEqual("https://github.com/Jvaxx/kOSIm/releases/download/v2.3.4.105/kOS-v2.3.4.105.zip",
                         module["download"])
        self.assertEqual(len(data), module["download_size"])
        self.assertEqual(hashlib.sha256(data).hexdigest().upper(), module["download_hash"]["sha256"])
        self.assertEqual(hashlib.sha1(data).hexdigest().upper(), module["download_hash"]["sha1"])
        with zipfile.ZipFile(path) as archive:
            self.assertEqual(sum(info.file_size for info in archive.infolist()), module["install_size"])
        self.assertEqual("2026-10-21T12:00:00Z", module["release_date"])
        self.assertFalse([key for key in module if key.startswith(("$", "x_netkan"))])
        for key in ("identifier", "provides", "conflicts", "depends", "install", "license"):
            self.assertEqual(self.netkan[key], module[key])

    def test_keeps_a_wider_range(self):
        path = self.make_zip(version_json={"VERSION": "2.3.4.105", "KSP_VERSION_MIN": "1.11",
                                           "KSP_VERSION_MAX": "1.12"})
        module = ckan.inflate(self.netkan, path)
        self.assertEqual(("1.11", "1.12"), (module["ksp_version_min"], module["ksp_version_max"]))

    def test_rejects_packages_ckan_would_install_incorrectly(self):
        cases = {
            "outside": self.make_zip("kOS-v2.3.4.105.zip", extra=["README.txt"]),
            "two version files": self.make_zip("kOS-v2.3.4.105.zip", extra=["GameData/kOS/Other.version"]),
            "name/version mismatch": self.make_zip("kOS-v2.3.4.106.zip"),
            "test name": self.make_zip("kOS-v2.3.4.105-dirty.zip"),
            "no range": self.make_zip(version_json={"VERSION": "2.3.4.105"}),
        }
        for label, path in cases.items():
            with self.subTest(label), self.assertRaises(ValueError):
                ckan.inflate(self.netkan, path)

    def test_test_mode_accepts_local_names_and_urls(self):
        path = self.make_zip("kOS-v2.3.4.105-dirty.zip")
        module = ckan.inflate(self.netkan, path, "http://127.0.0.1:1/x.zip", test=True)
        self.assertEqual("http://127.0.0.1:1/x.zip", module["download"])


    def test_solver_upgrade_package_changes_only_version_metadata(self):
        source = self.make_zip(extra=["GameData/kOS/SOURCE.txt"])
        target = Path(self.temp.name) / "kOS-v2.3.4.106-solvertest.zip"
        self.assertEqual("2.3.4.106", solver.next_version("2.3.4.105"))
        solver.bumped_package(source, target, "2.3.4.106")
        module = ckan.inflate(self.netkan, target, test=True)
        self.assertEqual("2.3.4.106", module["version"])
        with zipfile.ZipFile(source) as old, zipfile.ZipFile(target) as new:
            self.assertEqual(old.namelist(), new.namelist())
            self.assertEqual(old.read("GameData/kOS/Plugins/kOS.dll"), new.read("GameData/kOS/Plugins/kOS.dll"))
            self.assertIn(b"Do not distribute", new.read("GameData/kOS/SOURCE.txt"))


if __name__ == "__main__":
    unittest.main()
