"""Regression coverage for source and generated-build version consistency."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("versions", Path(__file__).with_name("check-version.py"))
versions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(versions)


class VersionCheckTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.props = self.root / "kOS.props"
        self.props.write_text('''<Project><PropertyGroup>
<KSPBT_ModVersion>2.3.4.123</KSPBT_ModVersion>
<KOSIM_UpstreamVersion>2.3.4.9</KOSIM_UpstreamVersion>
<KOSIM_UpstreamCommit>aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa</KOSIM_UpstreamCommit>
<Version>$(KSPBT_ModVersion)</Version>
</PropertyGroup></Project>''', encoding="utf-8")
        self.changelog = self.root / "CHANGELOG.md"
        self.changelog.write_text("## kOS Improved 2.3.4.123 (Unreleased)\n"
                                  "## kOS Improved 2.3.4.122 (2026-01-01)\n"
                                  "## Upstream kOS 2.3.4.9 - 2026-01-01\n", encoding="utf-8")
        for name in ("kOS", "kOS.Safe"):
            self.assembly_path(name).parent.mkdir(parents=True)
            self.assembly_path(name).write_text('''[assembly: System.Reflection.AssemblyVersionAttribute("2.3.4.123")]
[assembly: System.Reflection.AssemblyFileVersionAttribute("2.3.4.123")]
[assembly: System.Reflection.AssemblyInformationalVersionAttribute("2.3.4.123+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
[assembly: KSPAssembly("kOS", 2, 3, 4)]
''', encoding="utf-8")
        self.metadata = self.root / "Resources/GameData/kOS/kOS.version"
        self.metadata.parent.mkdir(parents=True)
        self.metadata.write_text(json.dumps({"VERSION": "2.3.4.123"}), encoding="utf-8")

    def assembly_path(self, name):
        return self.root / "src" / name / "obj/Release/net48" / f"{name}.AssemblyInfo.cs"

    def replace(self, path, old, new):
        text = path.read_text(encoding="utf-8")
        self.assertIn(old, text)
        path.write_text(text.replace(old, new), encoding="utf-8")

    def test_accepts_different_upstream_and_fork_fourth_fields(self):
        self.assertIn("kOSIM 2.3.4.123; upstream 2.3.4.9", versions.check_version(self.root, built=True))

    def test_rejects_wrong_source_prefix(self):
        self.replace(self.props, "2.3.4.123", "1.5.1.123")
        with self.assertRaisesRegex(ValueError, "does not match upstream"):
            versions.check_version(self.root)

    def test_rejects_malformed_or_out_of_range_versions(self):
        for value in ("1.2.3", "1.2.3.4.5", "1.2.3.-1", "1.2.3.65535", "01.2.3.4", "$(OtherVersion)"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                versions.version_parts(value)

    def test_rejects_nonfork_build_counter(self):
        self.replace(self.props, "2.3.4.123", "2.3.4.9")
        with self.assertRaisesRegex(ValueError, "counter"):
            versions.check_version(self.root)

    def test_rejects_missing_duplicate_or_short_source_commit(self):
        original = self.props.read_text(encoding="utf-8")
        element = "<KOSIM_UpstreamCommit>" + "a" * 40 + "</KOSIM_UpstreamCommit>"
        for replacement in ("", element * 2, element.replace("a" * 40, "8f281a45")):
            self.props.write_text(original.replace(element, replacement), encoding="utf-8")
            with self.subTest(replacement=replacement), self.assertRaises(ValueError):
                versions.check_version(self.root)

    def test_rejects_independent_sdk_version(self):
        self.replace(self.props, "$(KSPBT_ModVersion)", "2.3.4.123")
        with self.assertRaisesRegex(ValueError, "single"):
            versions.check_version(self.root)

    def test_rejects_stale_latest_changelog(self):
        self.replace(self.changelog, "2.3.4.123", "2.3.4.122")
        with self.assertRaisesRegex(ValueError, "Latest fork changelog"):
            versions.check_version(self.root)

    def test_rejects_unlabelled_upstream_changelog(self):
        self.replace(self.changelog, "Upstream kOS 2.3.4.9 - 2026-01-01", "Unreleased")
        with self.assertRaisesRegex(ValueError, "exact upstream"):
            versions.check_version(self.root)

    def test_checks_both_assembly_and_file_versions_in_both_projects(self):
        for name in ("kOS", "kOS.Safe"):
            path = self.assembly_path(name)
            original = path.read_text(encoding="utf-8")
            for attribute in ("AssemblyVersion", "AssemblyFileVersion"):
                self.replace(path, f'{attribute}Attribute("2.3.4.123")', f'{attribute}Attribute("1.5.1.102")')
                with self.subTest(name=name, attribute=attribute), self.assertRaisesRegex(ValueError, attribute):
                    versions.check_version(self.root, built=True)
                path.write_text(original, encoding="utf-8")

    def test_rejects_missing_informational_commit(self):
        self.replace(self.assembly_path("kOS.Safe"), "+" + "a" * 40, "")
        with self.assertRaisesRegex(ValueError, "informational version"):
            versions.check_version(self.root, built=True)

    def test_rejects_old_ksp_identity(self):
        self.replace(self.assembly_path("kOS"), 'KSPAssembly("kOS", 2, 3, 4)', 'KSPAssembly("kOS", 1, 5, 1)')
        with self.assertRaisesRegex(ValueError, "KSPAssembly"):
            versions.check_version(self.root, built=True)

    def test_rejects_stale_version_file(self):
        self.replace(self.metadata, "2.3.4.123", "1.5.1.102")
        with self.assertRaisesRegex(ValueError, "kOS.version does not match"):
            versions.check_version(self.root, built=True)

    def test_rejects_update_urls_even_when_empty(self):
        for key in ("URL", "DOWNLOAD"):
            self.metadata.write_text(json.dumps({"VERSION": "2.3.4.123", key: ""}), encoding="utf-8")
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, "update URLs"):
                versions.check_version(self.root, built=True)

    def test_source_check_needs_no_build_and_built_check_requires_it(self):
        self.assembly_path("kOS").unlink()
        self.assertIn("source versions consistent", versions.check_version(self.root))
        with self.assertRaises(OSError):
            versions.check_version(self.root, built=True)


if __name__ == "__main__":
    unittest.main()
