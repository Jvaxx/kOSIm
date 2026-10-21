"""Regression coverage for the deterministic release ZIP builder."""

import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("package", Path(__file__).with_name("package.py"))
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)

COMMIT_FILES = {
    "kOS.props": '''<Project><PropertyGroup>
<KSPBT_ModVersion>2.3.4.105</KSPBT_ModVersion>
<KOSIM_UpstreamVersion>2.3.4.1</KOSIM_UpstreamVersion>
<KOSIM_UpstreamCommit>aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa</KOSIM_UpstreamCommit>
<Version>$(KSPBT_ModVersion)</Version>
</PropertyGroup></Project>''',
    "src/kOS/kOS.csproj": '''<Project><ItemGroup><KSPVersionFile Include=".">
<KSP_Version>1.12</KSP_Version><KSP_Version_Min>1.12</KSP_Version_Min><KSP_Version_Max>1.12</KSP_Version_Max>
</KSPVersionFile></ItemGroup></Project>''',
    "CHANGELOG.md": "## kOS Improved 2.3.4.105 (Unreleased)\n## Upstream kOS 2.3.4.1 - 2026-01-01\n",
    "LICENSE.md": "GPLv3 text\n",
    "THIRD_PARTY_NOTICES.md": "notices\n",
    ".gitignore": "src/*/obj/\nResources/GameData/kOS/Plugins/*\n"
                  "!Resources/GameData/kOS/Plugins/ICSharpCode.SharpZipLib.dll\n"
                  "Resources/GameData/kOS/kOS.version\nResources/GameData/kOS/Parts/@thumbs/\n",
    "Resources/GameData/kOS/Readme": "readme\n",
    "Resources/GameData/kOS/Parts/kOSMachine1m/part.cfg": "PART {}\n",
    "Resources/GameData/kOS/Plugins/ICSharpCode.SharpZipLib.dll": "zip library",
    "Resources/GameData/kOS/Licenses/SharpZipLib-0.86.0.518-COPYING.txt": "GPLv2 text\n",
}


class PackageTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "repo"
        for name, text in COMMIT_FILES.items():
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
        self.git("init", "-q")
        self.git("add", "-A")
        self.git("-c", "user.name=test", "-c", "user.email=test@example.invalid",
                 "commit", "-q", "-m", "fixture")
        self.commit = self.git("rev-parse", "HEAD").strip()
        self.build()
        self.original_sharpziplib = dict(package.SHARPZIPLIB)
        package.SHARPZIPLIB["sha256"] = hashlib.sha256(b"zip library").hexdigest()
        self.addCleanup(package.SHARPZIPLIB.update, self.original_sharpziplib)

    def git(self, *args):
        return subprocess.run(["git", *args], cwd=self.root, check=True,
                              capture_output=True, text=True).stdout

    def build(self, commit=None):
        marker = f"2.3.4.105+{commit or self.commit}"
        for name in ("kOS", "kOS.Safe"):
            info = self.root / "src" / name / "obj/Release/net48" / f"{name}.AssemblyInfo.cs"
            info.parent.mkdir(parents=True, exist_ok=True)
            info.write_text(f'''[assembly: System.Reflection.AssemblyVersionAttribute("2.3.4.105")]
[assembly: System.Reflection.AssemblyFileVersionAttribute("2.3.4.105")]
[assembly: System.Reflection.AssemblyInformationalVersionAttribute("{marker}")]
[assembly: KSPAssembly("kOS", 2, 3, 4)]
''', encoding="utf-8")
            (self.mod / "Plugins" / f"{name}.dll").write_bytes(b"MZ..." + marker.encode() + b"...")
        (self.mod / "kOS.version").write_text(json.dumps(
            {"NAME": "kOS", "VERSION": "2.3.4.105", "KSP_VERSION": "1.12",
             "KSP_VERSION_MIN": "1.12", "KSP_VERSION_MAX": "1.12"}), encoding="utf-8")
        # Present in a dev build or game copy, but never part of a release.
        (self.mod / "Plugins/PluginData/kOS").mkdir(parents=True, exist_ok=True)
        (self.mod / "Plugins/PluginData/kOS/config.xml").write_text("user settings")
        (self.mod / "Plugins/kOS.pdb").write_bytes(b"symbols")
        (self.mod / "Parts/@thumbs").mkdir(exist_ok=True)
        (self.mod / "Parts/@thumbs/kOSMachine1m_icon.png").write_bytes(b"png")

    @property
    def mod(self):
        return self.root / "Resources/GameData/kOS"

    def package(self, name="out", **kwargs):
        return package.package(self.root, Path(self.temp.name) / name, **kwargs)

    def test_packages_tracked_built_and_license_files_only(self):
        path = self.package()
        self.assertEqual("kOS-v2.3.4.105.zip", path.name)
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            source = archive.read("GameData/kOS/SOURCE.txt").decode()
            self.assertEqual(b"GPLv3 text\n", archive.read("GameData/kOS/LICENSE.md"))
        self.assertEqual(sorted(names), names)
        self.assertEqual({
            "GameData/kOS/" + name for name in (
                "Readme", "Parts/kOSMachine1m/part.cfg", "Plugins/ICSharpCode.SharpZipLib.dll",
                "Licenses/SharpZipLib-0.86.0.518-COPYING.txt", "Plugins/kOS.dll",
                "Plugins/kOS.Safe.dll", "kOS.version", "LICENSE.md",
                "THIRD_PARTY_NOTICES.md", "SOURCE.txt")}, set(names))
        self.assertIn(f"commit: {self.commit}", source)
        self.assertIn("tag:    v2.3.4.105", source)
        self.assertNotIn("UNCOMMITTED", source)

    def test_same_commit_and_build_give_identical_archives(self):
        first, second = self.package("a"), self.package("b")
        self.assertEqual(first.read_bytes(), second.read_bytes())

    def test_never_updates_an_existing_archive(self):
        self.package()
        with self.assertRaises(FileExistsError):
            self.package()

    def test_dirty_tree_needs_opt_in_and_is_labelled(self):
        (self.mod / "Readme").write_text("changed\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Working tree"):
            self.package()
        path = self.package(allow_dirty=True)
        self.assertEqual("kOS-v2.3.4.105-dirty.zip", path.name)
        with zipfile.ZipFile(path) as archive:
            self.assertIn("UNCOMMITTED", archive.read("GameData/kOS/SOURCE.txt").decode())
            self.assertEqual(b"changed\n", archive.read("GameData/kOS/Readme"))

    def test_rejects_dll_from_another_commit(self):
        self.build(commit="b" * 40)
        with self.assertRaisesRegex(ValueError, "not built from commit"):
            self.package()

    def test_rejects_missing_build_output(self):
        (self.mod / "Plugins/kOS.Safe.dll").unlink()
        with self.assertRaisesRegex(ValueError, "Missing build outputs"):
            self.package()

    def test_rejects_unaudited_sharpziplib(self):
        package.SHARPZIPLIB["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "SharpZipLib"):
            self.package()

    def test_rejects_stale_version_file_range(self):
        metadata = json.loads((self.mod / "kOS.version").read_text(encoding="utf-8"))
        metadata["KSP_VERSION_MIN"] = "1.8"
        (self.mod / "kOS.version").write_text(json.dumps(metadata), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "KSP_VERSION_MIN"):
            self.package()


class RepositoryPackageInputsTest(unittest.TestCase):
    def test_audited_sharpziplib_and_license_are_tracked(self):
        mod = Path(__file__).resolve().parent.parent / "Resources/GameData/kOS"
        dll = (mod / package.SHARPZIPLIB["path"]).read_bytes()
        self.assertEqual(package.SHARPZIPLIB["sha256"], hashlib.sha256(dll).hexdigest())
        license_text = (mod / package.SHARPZIPLIB["license"]).read_text(encoding="utf-8")
        self.assertIn("GNU GENERAL PUBLIC LICENSE", license_text)
        self.assertIn("Version 2, June 1991", license_text)
        notices = (mod.parent.parent.parent / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8")
        self.assertIn(package.SHARPZIPLIB["sha256"], notices)
        self.assertIn(package.SHARPZIPLIB["commit"], notices)


if __name__ == "__main__":
    unittest.main()
