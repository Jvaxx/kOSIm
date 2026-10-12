#!/usr/bin/env python3
"""Gate NUnit 2 XML without hiding the recorded upstream failures."""

import argparse
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def check_results(report_path, baseline, runner_exit_code):
    minimum = baseline["minimum_tests"]
    entries = baseline["failures"]
    known = {entry["name"]: entry for entry in entries}
    if minimum < 1 or len(known) != len(entries):
        raise ValueError("Invalid baseline: positive minimum and unique failure names required")
    if any(not entry["reason"] or not entry["message_contains"] for entry in entries):
        raise ValueError("Every known failure needs a reason and message checks")

    root = ET.parse(report_path).getroot()
    if root.tag != "test-results":
        raise ValueError("Expected NUnit 2 test-results XML")
    cases = list(root.iter("test-case"))
    names = [case.attrib["name"] for case in cases]
    if len(cases) < minimum or len(set(names)) != len(names):
        raise ValueError("Missing or duplicate tests: discovered {}, minimum {}".format(len(cases), minimum))

    failures = {}
    for case in cases:
        name = case.attrib["name"]
        result = case.get("result")
        if case.get("executed") != "True":
            raise ValueError("Test was not executed: " + name)
        if result == "Success" and case.get("success") == "True":
            continue
        if result != "Failure" or case.get("success") != "False":
            raise ValueError("Unexpected test result {}: {}".format(result, name))
        if name not in known:
            raise ValueError("Unexpected failing test: " + name)
        message = case.findtext("failure/message", default="")
        if not all(fragment in message for fragment in known[name]["message_contains"]):
            raise ValueError("Known failure changed its assertion: " + name)
        failures[name] = message

    missing = set(known) - set(failures)
    if missing:
        raise ValueError("Baseline failures no longer failing; investigate and update the baseline: " + ", ".join(sorted(missing)))
    expected_counts = {
        "total": len(cases), "failures": len(failures), "errors": 0,
        "not-run": 0, "inconclusive": 0, "ignored": 0, "skipped": 0, "invalid": 0,
    }
    for key, expected in expected_counts.items():
        if int(root.attrib[key]) != expected:
            raise ValueError("Inconsistent report count: " + key)
    if runner_exit_code != len(failures):
        raise ValueError("Runner exit {} does not match reported failures {}".format(runner_exit_code, len(failures)))

    lines = [
        "Headless tests: {} discovered/executed, {} passed, {} known upstream failures, 0 skipped.".format(
            len(cases), len(cases) - len(failures), len(failures)),
        "Regression gate passed (not an all-green test suite)." if failures else "All tests passed.",
    ]
    lines.extend("- KNOWN FAILURE: " + name for name in sorted(failures))
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--runner-exit-code", type=int, required=True)
    args = parser.parse_args()
    try:
        baseline = json.loads(args.baseline.read_text(encoding="utf-8"))
        summary = check_results(args.report, baseline, args.runner_exit_code)
    except (OSError, ET.ParseError, ValueError, KeyError, TypeError) as error:
        summary = "Headless regression gate FAILED: {}\n".format(error)
        status = 1
    else:
        status = 0
    print(summary, end="")
    return status


if __name__ == "__main__":
    sys.exit(main())
