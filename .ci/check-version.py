#!/usr/bin/env python3
"""Check kOSIM source versions; --built also checks full Release build metadata.

No game assemblies or third-party Python packages are needed. Headless test
builds are not full mod builds and must not be used with --built.
"""

import argparse
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parent.parent


def version_parts(value):
    if not re.fullmatch(r"(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){3}", value):
        raise ValueError(f"Expected a four-field numeric version, got {value!r}")
    parts = tuple(map(int, value.split(".")))
    if any(part > 65534 for part in parts):
        raise ValueError(f"Version exceeds assembly field limits: {value}")
    return parts


def check_version(root, built=False):
    props = ET.parse(root / "kOS.props").getroot()

    def property_value(name):
        values = props.findall(f"./PropertyGroup/{name}")
        if len(values) != 1 or not values[0].text:
            raise ValueError(f"Expected one literal {name} in kOS.props")
        return values[0].text.strip()

    version = property_value("KSPBT_ModVersion")
    upstream = property_value("KOSIM_UpstreamVersion")
    parts = version_parts(version)
    if parts[:3] != version_parts(upstream)[:3]:
        raise ValueError(f"Fork prefix {version} does not match upstream {upstream}")
    if parts[3] < 100:
        raise ValueError("The kOSIM build counter must be at least 100")
    if not re.fullmatch(r"[0-9a-f]{40}", property_value("KOSIM_UpstreamCommit")):
        raise ValueError("KOSIM_UpstreamCommit must be a full source commit hash")
    if property_value("Version") != "$(KSPBT_ModVersion)":
        raise ValueError("Version must use the single KSPBT_ModVersion source")

    changelog = (root / "CHANGELOG.md").read_text(encoding="utf-8")
    entries = re.findall(r"^## kOS Improved ([0-9.]+) ", changelog, re.MULTILINE)
    if not entries or entries[0] != version:
        raise ValueError("Latest fork changelog version does not match kOS.props")
    if not re.search(rf"^## Upstream kOS {re.escape(upstream)} - ", changelog, re.MULTILINE):
        raise ValueError("Missing changelog heading for the exact upstream release")

    if built:
        for name in ("kOS", "kOS.Safe"):
            path = root / "src" / name / "obj/Release/net48" / f"{name}.AssemblyInfo.cs"
            attributes = path.read_text(encoding="utf-8-sig")
            for attribute in ("AssemblyVersion", "AssemblyFileVersion"):
                values = re.findall(rf'{attribute}Attribute\("([^"\n]+)"\)', attributes)
                if values != [version]:
                    raise ValueError(f"{name} generated {attribute} does not match {version}")
            info = re.findall(r'AssemblyInformationalVersionAttribute\("([^"\n]+)"\)', attributes)
            if len(info) != 1 or not re.fullmatch(rf"{re.escape(version)}\+[0-9a-f]{{40}}", info[0]):
                raise ValueError(f"{name} informational version must include {version} and the commit")
            if name == "kOS":
                identity = ', '.join(map(str, parts[:3]))
                if f'[assembly: KSPAssembly("kOS", {identity})]' not in attributes:
                    raise ValueError("Generated KSPAssembly identity does not match the fork prefix")
        metadata = json.loads((root / "Resources/GameData/kOS/kOS.version").read_text(encoding="utf-8-sig"))
        if metadata.get("VERSION") != version:
            raise ValueError("Generated kOS.version does not match kOS.props")
        if "URL" in metadata or "DOWNLOAD" in metadata:
            raise ValueError("Generated kOS.version must not contain update URLs")

    return f"kOSIM {version}; upstream {upstream}; {'source and generated build' if built else 'source'} versions consistent"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--built", action="store_true", help="also check metadata from a full Release solution build")
    args = parser.parse_args()
    try:
        print(check_version(ROOT, args.built))
    except (OSError, ValueError, ET.ParseError) as error:
        print(f"Version check failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
