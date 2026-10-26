# 0003 — Run every workflow of a folder

- **Status:** Draft
- **Roadmap:** M1-7
- **Repositories:** tinyprobe
- **PRs:** —

## Problem

`tinyprobe run` takes one workflow. A project has several (the guide asks for one per feature or resource), and a migration from Postman produces one per folder. Today:

- an agent runs them one by one, and says "all pass" after checking only some of them;
- CI needs a shell loop, and the loop must remember the worst exit code (`scripts/dev.sh e2e` does exactly that);
- there is no single summary of the whole suite.

## Proposal

`run` accepts a folder, and more than one path:

```text
$ tinyprobe run tinyprobe/workflows -e tinyprobe/env.local.yaml
$ tinyprobe run tinyprobe -e tinyprobe/env.local.yaml           # api/ and env files are skipped
$ tinyprobe run tinyprobe/workflows/auth.yaml tinyprobe/workflows/users.yaml
```

Each workflow prints its usual output. A summary of the suite comes last:

```text
  ⚡ tinyprobe 0.12.0

  workflow : Authentication
  file     : tinyprobe/workflows/auth.yaml
  ...
   PASSED   7 steps

  workflow : Users
  file     : tinyprobe/workflows/users.yaml
  ...
   FAILED   1 failed

  ✓ tinyprobe/workflows/auth.yaml      7/7 steps    0.41s
  ✗ tinyprobe/workflows/users.yaml     3/12 steps   1.02s   Create a user: expected status 201, got 500
  ✓ tinyprobe/workflows/catalog.yaml   2/2 steps    0.12s

  workflows...........: 2/3 passed
  steps...............: 12/21 passed
  suite_duration......: 1.55s

   FAILED   1 of 3 workflows
```

Rules:

- **Which files.** A folder is read recursively. Every `.yaml` / `.yml` file with a top-level `steps` key is a workflow; other files (API files, env files) are skipped. Folders whose name starts with `.` are skipped. A file named on the command line must be a workflow.
- **Order.** Files of a folder in ordinal order of their path; paths given on the command line in the order given. A file reached twice runs once.
- **Validation first.** Every workflow and its imports are loaded before any request. If one is invalid, nothing runs: all the problems of all the files are printed, exit code `2`.
- **Independent workflows.** Each workflow starts from zero ([decision 0003](../decisions/0003-endpoints-declare-workflows-run.md)): `-e` and `--var` apply to each, and values saved in one workflow are not visible in the next. `-v` and `--reporter` apply to all.
- **A failure does not stop the suite.** A failing workflow stops at its failing step, as today, and the next workflow runs.
- **Exit codes** (unchanged meaning): `0` every workflow passed, `1` at least one failed, `2` invalid arguments, an invalid file, or a folder without workflows.
- **One workflow, same output as today.** With a single workflow file, the output and the ndjson events are exactly those of 0.11.0: no suite summary, no suite events.

## Validation

```text
Error: No workflow files in tinyprobe/api (a workflow has a top-level 'steps' key).
Error: tinyprobe/api/backend.yaml is not a workflow (it has no 'steps'): run a workflow that imports it, or 'tinyprobe call' one endpoint.
```

Invalid workflows are reported as today, one block per file, with `file:line:column`.

## Protocol

With a folder or several paths, `--reporter ndjson` wraps the events of each workflow, unchanged, between two new events:

```json
{"event":"suite_started","protocol":1,"version":"0.12.0","files":["/home/me/p/tinyprobe/workflows/auth.yaml","/home/me/p/tinyprobe/workflows/users.yaml"]}
{"event":"run_started", ...}
...
{"event":"run_finished", ...}
{"event":"run_started", ...}
...
{"event":"run_finished", ...}
{"event":"suite_finished","success":false,"workflows":[{"file":"/home/me/p/tinyprobe/workflows/auth.yaml","success":true},{"file":"/home/me/p/tinyprobe/workflows/users.yaml","success":false}],"summary":{"total_workflows":2,"passed_workflows":1,"failed_workflows":1,"total_steps":19,"passed_steps":10,"failed_steps":1,"not_run_steps":8,"duration_ms":1430.2}}
```

- New events only: `protocol` stays `1`. `run_finished` stays the last event of a single-workflow run; in a suite, `suite_finished` is the last event.
- An invalid suite is one `invalid` event, as today; each error already has its `file`.

## Extension

No change needed: the extension runs one workflow at a time. Later, the Test Explorer can run a folder with one process (a separate task).

## Docs

- `docs/workflow-format.md`: remove "Running a whole folder" from "Not supported yet".
- `docs/llm-guide.md`: section 1, and section 8 step 7: "run every workflow with `tinyprobe run tinyprobe/workflows -e tinyprobe/env.local.yaml`; you are done when it exits `0`". Section 12.1 step 6 and the checklist: the final run is the whole folder.
- `README.md`: Usage, and the CI example.
- `docs/protocol.md`: the two events.
- `scripts/dev.sh e2e` and CI run `docs/example/workflows` as one folder, so the feature runs end to end in CI.

## Out of scope

- Running workflows in parallel.
- `--fail-fast` (stop at the first failing workflow), and filters (`--only auth`).
- Sharing values between workflows (ruled out by decision 0003; a shared login is M4-5).
- The JUnit report (M3-1), which will use the same suite model.

## Acceptance criteria

- [ ] `tinyprobe run <folder>` and `tinyprobe run <file> <file>...` run as above, with the suite summary and exit codes `0` / `1` / `2`.
- [ ] Nothing is sent when any workflow is invalid; all problems are listed.
- [ ] A single workflow file gives byte-for-byte the output and events of today (existing tests unchanged).
- [ ] Unit tests: file discovery (recursion, skipped files and folders, order, duplicates), the suite summary, the new events.
- [ ] `scripts/dev.sh e2e` runs `docs/example/workflows` as one folder.
- [ ] `scripts/dev.sh check` passes.

## Open questions

1. **Recursive by default?** Recommended: yes, skipping non-workflow files, so `tinyprobe run tinyprobe` works from the layout of the guide. The alternative is only the files directly in the folder.
2. **Several paths on the command line?** Recommended: yes. It costs little, and shell globs (`tinyprobe run tinyprobe/workflows/*.yaml`) then work too.
3. **Continue after a failing workflow?** Recommended: yes, so one run shows every failure. `--fail-fast` can come later if CI needs it.
