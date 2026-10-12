"""Tests for the local regression gate; no external Python dependencies."""

import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location("gate", Path(__file__).with_name("check-test-results.py"))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


class ResultGateTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.report = Path(self.temp.name) / "nunit.xml"
        self.baseline = {
            "minimum_tests": 2,
            "failures": [{"name": "Known", "message_contains": ["original assertion"], "reason": "upstream"}],
        }
        self.root = ET.Element("test-results", {
            "total": "2", "failures": "1", "errors": "0", "not-run": "0",
            "inconclusive": "0", "ignored": "0", "skipped": "0", "invalid": "0",
        })
        self.passing = ET.SubElement(self.root, "test-case", {
            "name": "Passing", "executed": "True", "success": "True", "result": "Success",
        })
        self.failing = ET.SubElement(self.root, "test-case", {
            "name": "Known", "executed": "True", "success": "False", "result": "Failure",
        })
        self.message = ET.SubElement(ET.SubElement(self.failing, "failure"), "message")
        self.message.text = "original assertion"

    def check(self, exit_code=1):
        ET.ElementTree(self.root).write(self.report)
        return gate.check_results(self.report, self.baseline, exit_code)

    def test_reports_known_failures_without_claiming_all_green(self):
        summary = self.check()
        self.assertIn("2 discovered/executed, 1 passed, 1 known upstream failures, 0 skipped", summary)
        self.assertIn("not an all-green", summary)

    def test_all_green_when_baseline_is_empty(self):
        self.baseline["failures"] = []
        self.failing.set("result", "Success")
        self.failing.set("success", "True")
        self.root.set("failures", "0")
        self.assertIn("All tests passed", self.check(exit_code=0))

    def test_rejects_new_failure(self):
        self.passing.set("result", "Failure")
        self.passing.set("success", "False")
        with self.assertRaisesRegex(ValueError, "Unexpected failing test"):
            self.check(exit_code=2)

    def test_rejects_changed_assertion(self):
        self.message.text = "a different failure"
        with self.assertRaisesRegex(ValueError, "changed its assertion"):
            self.check()

    def test_requires_baseline_update_when_known_failure_is_fixed(self):
        self.failing.set("result", "Success")
        self.failing.set("success", "True")
        with self.assertRaisesRegex(ValueError, "no longer failing"):
            self.check(exit_code=0)

    def test_rejects_unexecuted_and_nonfailure_results(self):
        for result in ["Ignored", "Skipped", "Inconclusive", "Error", "Invalid"]:
            with self.subTest(result=result):
                self.passing.set("result", result)
                with self.assertRaises(ValueError):
                    self.check()
        self.passing.set("result", "Success")
        self.passing.set("executed", "False")
        with self.assertRaisesRegex(ValueError, "not executed"):
            self.check()

    def test_rejects_duplicate_test_names(self):
        self.passing.set("name", "Known")
        with self.assertRaisesRegex(ValueError, "duplicate tests"):
            self.check()

    def test_rejects_missing_or_zero_tests(self):
        self.root.remove(self.passing)
        with self.assertRaisesRegex(ValueError, "Missing"):
            self.check()
        self.root.remove(self.failing)
        with self.assertRaisesRegex(ValueError, "Missing"):
            self.check()

    def test_rejects_inconsistent_counts_including_fixture_errors(self):
        for key in self.root.attrib:
            original = self.root.get(key)
            self.root.set(key, str(int(original) + 1))
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, "Inconsistent report count"):
                self.check()
            self.root.set(key, original)

    def test_rejects_wrong_runner_exit_including_crash(self):
        for code in [0, 2, -1, 127]:
            with self.subTest(code=code), self.assertRaisesRegex(ValueError, "Runner exit"):
                self.check(exit_code=code)

    def test_rejects_invalid_baseline(self):
        self.baseline["failures"].append(copy.deepcopy(self.baseline["failures"][0]))
        with self.assertRaisesRegex(ValueError, "Invalid baseline"):
            self.check()

    def test_rejects_missing_and_malformed_report(self):
        with self.assertRaises(OSError):
            gate.check_results(self.report, self.baseline, 1)
        self.report.write_text("not xml", encoding="utf-8")
        with self.assertRaises(ET.ParseError):
            gate.check_results(self.report, self.baseline, 1)


if __name__ == "__main__":
    unittest.main()
