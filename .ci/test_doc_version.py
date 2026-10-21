"""Exercise the real Sphinx config without requiring Sphinx or its theme."""

from pathlib import Path
import runpy
import sys
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
CONFIG = ROOT / "doc/source/conf.py"


def load_config(path):
    with patch.object(sys, "path", sys.path.copy()):
        return runpy.run_path(str(path))


class DocVersionTest(unittest.TestCase):
    def test_documentation_matches_actual_fork_and_upstream_versions(self):
        props = ET.parse(ROOT / "kOS.props")
        fork = props.findtext("./PropertyGroup/KSPBT_ModVersion")
        upstream = props.findtext("./PropertyGroup/KOSIM_UpstreamVersion")
        config = load_config(CONFIG)
        self.assertEqual("kOS Improved", config["project"])
        self.assertEqual(fork, config["release"])
        self.assertEqual(".".join(fork.split(".")[:3]), config["version"])
        self.assertIn(".. |upstream_release| replace:: " + upstream, config["rst_epilog"])

    def test_documentation_reads_props_relative_to_config_not_cwd(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "doc/source/conf.py"
            path.parent.mkdir(parents=True)
            path.write_text(CONFIG.read_text(encoding="utf-8"), encoding="utf-8")
            (root / "kOS.props").write_text('''<Project><PropertyGroup>
<KSPBT_ModVersion>2.3.4.123</KSPBT_ModVersion>
<KOSIM_UpstreamVersion>2.3.4.9</KOSIM_UpstreamVersion>
</PropertyGroup></Project>''', encoding="utf-8")
            config = load_config(path)
            self.assertEqual("2.3.4.123", config["release"])
            self.assertEqual("2.3.4", config["version"])
            self.assertIn("replace:: 2.3.4.9", config["rst_epilog"])


if __name__ == "__main__":
    unittest.main()
