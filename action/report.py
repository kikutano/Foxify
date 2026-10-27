"""Job summary and annotations of the tinyprobe GitHub Action, from the JUnit report of `tinyprobe run --junit`.

    python3 action/report.py <junit.xml>

Environment: INPUT_SUMMARY and INPUT_ANNOTATIONS ("true" or "false"), GITHUB_STEP_SUMMARY (set by GitHub).
Annotations are workflow commands printed on standard output: GitHub shows them on the step's line of the
workflow file, in the run and in the pull request.
"""
import os
import re
import sys
import xml.etree.ElementTree as ET

PROBLEM = re.compile(r"^(?P<file>.+?):(?P<line>\d+):(?P<column>\d+): (?P<message>.*)$")


def escape_data(text):
    return text.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def escape_property(text):
    return escape_data(text).replace(":", "%3A").replace(",", "%2C")


def annotation(file, line, title, message):
    properties = [f"file={escape_property(file)}"]
    if line:
        properties.append(f"line={line}")
    properties.append(f"title={escape_property(title)}")
    return f"::error {','.join(properties)}::{escape_data(message)}"


def workflows(count):
    return f"{count} workflow" if count == 1 else f"{count} workflows"


def seconds(value):
    return f"{float(value or 0):.2f}s"


def main(path):
    if not os.path.exists(path):
        print(f"::warning::tinyprobe wrote no JUnit report ({path}): see the output of the run above.")
        return

    root = ET.parse(path).getroot()
    suites = root.findall("testsuite")
    summary_lines = []
    failures = []
    annotations = []

    total = sum(int(s.get("tests", 0)) for s in suites)
    failed = sum(int(s.get("failures", 0)) + int(s.get("errors", 0)) for s in suites)
    skipped = sum(int(s.get("skipped", 0)) for s in suites)
    passed = total - failed - skipped

    if failed == 0:
        summary_lines.append(f"### ✅ tinyprobe: {passed} of {total} steps passed in {workflows(len(suites))}")
    else:
        not_run = f", {skipped} not run" if skipped else ""
        summary_lines.append(f"### ❌ tinyprobe: {failed} failed{not_run}, {passed} of {total} steps passed in {workflows(len(suites))}")
    summary_lines += ["", "| | Workflow | File | Steps | Time |", "|---|---|---|---|---|"]

    for suite in suites:
        tests = int(suite.get("tests", 0))
        bad = int(suite.get("failures", 0)) + int(suite.get("errors", 0))
        ok = tests - bad - int(suite.get("skipped", 0))
        icon = "✅" if bad == 0 else "❌"
        summary_lines.append(f"| {icon} | {suite.get('name', '')} | `{suite.get('file', '')}` | {ok}/{tests} | {seconds(suite.get('time'))} |")

        for case in suite.findall("testcase"):
            result = case.find("failure")
            if result is None:
                result = case.find("error")
            if result is None:
                continue

            file = case.get("file", suite.get("file", ""))
            line = case.get("line")
            message = result.get("message", "failed")
            title = f"tinyprobe: {suite.get('name', '')} › {case.get('name', '')}"
            location = f"{file}:{line}" if line else file
            failures.append(f"- **{suite.get('name', '')} › {case.get('name', '')}** (`{location}`): {message}")

            if result.get("type") == "invalid":
                # An invalid file: one annotation per problem, where the problem is.
                for problem in (result.text or "").splitlines():
                    match = PROBLEM.match(problem)
                    if match:
                        annotations.append(annotation(match["file"], match["line"], "tinyprobe: invalid file", match["message"]))
            else:
                details = (result.text or "").strip()
                annotations.append(annotation(file, line, title, f"{message}\n\n{details}" if details and details != message else message))

    if failures:
        summary_lines += ["", "#### Failures", ""] + failures

    if os.environ.get("INPUT_SUMMARY", "true") == "true" and os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            summary.write("\n".join(summary_lines) + "\n")

    if os.environ.get("INPUT_ANNOTATIONS", "true") == "true":
        for line in annotations:
            print(line)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "tinyprobe-report.xml")
