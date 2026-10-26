# 0007 — JUnit XML report

- **Status:** Draft
- **Roadmap:** M3-1
- **Repositories:** tinyprobe
- **PRs:** —

## Problem

In CI, `tinyprobe run tinyprobe/workflows` fails the job with exit code `1`, and what failed is in the log, as text. To find it, someone opens the job, scrolls, and reads columns. Every CI server has a better place for this: a test tab that lists the failed tests by name, with their message, their history across runs, and in merge requests the tests that started failing. They all read the same format: JUnit XML (GitLab, Jenkins, Azure DevOps, CircleCI, Bitbucket, TeamCity, Buildkite, and GitHub through test-report actions).

## Proposal

```text
$ tinyprobe run tinyprobe/workflows -e tinyprobe/env.ci.yaml --junit reports/tinyprobe.xml
```

The console output (or `--reporter ndjson`) is unchanged; the report is written to the file as well, when the run ends, whether it passed or not.

```xml
<?xml version="1.0" encoding="utf-8"?>
<testsuites name="tinyprobe" tests="5" failures="1" errors="0" skipped="1" time="1.43">
  <testsuite name="Authentication" file="tinyprobe/workflows/auth.yaml" tests="2" failures="0" errors="0" skipped="0" time="0.41" timestamp="2026-10-27T09:12:03Z">
    <properties>
      <property name="tinyprobe.version" value="0.15.0" />
    </properties>
    <testcase name="Login" classname="Authentication" file="tinyprobe/workflows/auth.yaml" line="7" time="0.012" />
    <testcase name="Wrong password" classname="Authentication" file="tinyprobe/workflows/auth.yaml" line="15" time="0.009" />
  </testsuite>
  <testsuite name="Users" file="tinyprobe/workflows/users.yaml" tests="3" failures="1" errors="0" skipped="1" time="1.02" timestamp="2026-10-27T09:12:04Z">
    <testcase name="List users" classname="Users" file="tinyprobe/workflows/users.yaml" line="9" time="0.020" />
    <testcase name="Create a user" classname="Users" file="tinyprobe/workflows/users.yaml" line="21" time="0.031">
      <failure message="Expected status 201, got 500" type="status">POST http://localhost:8080/api/users -> 500

✓ $.email == "test.user@example.com"
✗ $.id exists  actual (missing)</failure>
    </testcase>
    <testcase name="Read the user" classname="Users" file="tinyprobe/workflows/users.yaml" line="33" time="0">
      <skipped message="Not run: an earlier step failed" />
    </testcase>
  </testsuite>
</testsuites>
```

Rules:

- **One `testsuite` per workflow, one `testcase` per call step**, in the order of the workflow. `sleep` steps are not test cases. `name` is the step's name (as in the console), `classname` the workflow's name, `file` and `line` where the step is, so CI servers and annotations can point to it.
- **`failure`** when a response came but did not pass: a status or a check. `message` is the first reason, the text is the request line, the status and every check with its actual value, as in the console.
- **`error`** when no response came: a network error, a timeout, an undefined variable. `message` is the reason.
- **`skipped`** for the steps after a failing one, which did not run.
- **No headers and no bodies** in the report: CI servers keep reports and show them to everyone who can open the pipeline, and requests carry tokens and passwords. The request line (method and URL) and the status are enough to find the step; the full exchange is in the console with `-v`, or in `--reporter ndjson`.
- **Times** in seconds with three decimals; `timestamp` in UTC.
- **Invalid files** (exit `2`): the report still is written, with one `testsuite` per file that failed to load, holding one `testcase` named `load` with an `error` that lists every problem (`file:line:column: message`). The CI server then says why nothing ran, instead of "no report found".
- The directory of the report is created if needed. A report that cannot be written is a usage error (exit `2`), after the run, with a message on standard error.
- `--junit` works for one workflow and for suites (spec 0003); not for `call` (usage error).

## Validation

```text
Error: Option '--junit' requires a value.
Error: Option '--junit' is not available for 'call'.
Error: Cannot write the JUnit report reports/tinyprobe.xml: <the system's message>
```

## Protocol

None: the ndjson events and `inspect --json` don't change. The XML is documented in `docs/protocol.md`, next to them.

## Extension

None.

## Docs

- `README.md`: a "Run in CI" section with GitHub Actions and GitLab CI examples that publish the report.
- `docs/protocol.md`: the JUnit report.
- `docs/llm-guide.md`: when an agent writes a CI job, it adds `--junit`.
- `--help`.

## Out of scope

- The GitHub Action (M3-2), annotations (M3-4) and the Docker image (M3-3): they use this report or the same results.
- Request and response bodies in the report (see the rules).
- Other formats (TAP, Allure, CTRF). They can come later on the same results.

## Acceptance criteria

- [ ] `tinyprobe run … --junit <file>` writes the report above, for passing, failing, skipped and invalid runs, for one workflow and for a suite.
- [ ] Unit tests on the XML: counts, failure / error / skipped, file and line, escaping, no headers or bodies.
- [ ] The report of `docs/example/workflows` is valid against a common JUnit XSD (checked in a test) and accepted by GitLab's and GitHub test-report actions' parsers: tried once by hand, noted in the PR.
- [ ] CI of this repository writes the report and uploads it as an artifact.
- [ ] `scripts/dev.sh check` passes.

## Open questions

1. **`--junit <file>` or `--reporter junit`?** Recommended: `--junit <file>`, an extra output. CI keeps the readable console log, and the report goes to a file, which is what CI servers read. The alternative is a third reporter on standard output, which replaces the console.
2. **Bodies in failures?** Recommended: no, as above, for secrets. The alternative is the response body of failed steps, cut at 2 KB, which helps debugging but can leak a token from a login response.
3. **`skipped` for steps that did not run?** Recommended: yes, so CI shows how many steps a failure hid. The alternative is to leave them out of the report.
