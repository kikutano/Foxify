# 0012 — Reusable step sequences (`use`)

- **Status:** Draft
- **Roadmap:** M4-5
- **Repositories:** tinyprobe
- **PRs:** —

## Problem

Every workflow starts from zero ([decision 0003](../decisions/0003-endpoints-declare-workflows-run.md)), so every workflow logs in itself. The login is the same two or three steps in each file:

```yaml
- name: Log in
  call: login
  with: { email: "${adminEmail}", password: "${adminPassword}" }
  expect: { status: 200 }
  save: { token: $.accessToken }
```

With ten workflows that is ten copies. When the login changes (a new field, a second step for a CSRF token, an OAuth2 exchange), every copy must change. The same holds for any setup used by several workflows: create a test customer, create a cart with a product, clean up. Postman users get this from collection-level scripts or `pm.sendRequest`; the guide today tells agents to copy the steps.

## Proposal

A **sequence** is a file of steps that workflows run with `use`.

```yaml
# tinyprobe/sequences/log-in.yaml
name: Log in
import:
  - ../api/backend.yaml
params:                                  # what a workflow passes with `with`
  email: admin@example.com               # with a default
  password:                              # required
sequence:
  - name: Log in
    call: login
    with: { email: "${email}", password: "${password}" }
    expect:
      status: 200
    save:
      token: $.accessToken               # visible to the workflow's next steps
```

```yaml
# tinyprobe/workflows/orders.yaml
name: Orders
import:
  - ../api/backend.yaml
steps:
  - use: ../sequences/log-in.yaml
    with:
      password: ${adminPassword}

  - name: List orders
    call: list_orders                    # authenticated with ${token}, saved by the sequence
    expect:
      status: 200
```

```text
  ✓ Log in › Log in       POST   200    35.10ms  /api/login
  ✓ List orders           GET    200     8.42ms  /api/orders
```

### The sequence file

- A YAML file with `sequence:` instead of `steps:`. The steps are written as in a workflow: `call`, `with`, `for_each`, `expect`, `until`, `save`, `sleep`, and `use` of another sequence.
- `params` declares what a `use` passes, as for an endpoint: `name: default`, or `name:` for a required one. Inside the sequence, a param is a variable (`${password}`). It is local to the sequence: the workflow does not see it afterwards.
- `name` (optional) prefixes the names of its steps in the report: `Log in › Log in`. Without it, the file name is used: `log-in › Log in`.
- `import` loads the API files that the sequence calls, as in a workflow. A sequence is checked on its own (`tinyprobe inspect tinyprobe/sequences/log-in.yaml`), so it imports what it uses. An API file imported by both the workflow and the sequence is loaded once.
- It has no `variables`, `settings`, `base_url`, `endpoints` or `auth` of its own. Those belong to the workflow, or to the API files.
- `tinyprobe run <folder>` skips sequence files, as it skips API files: they are not workflows. `tinyprobe run` on a sequence file is an error that says to `use` it from a workflow.

### `use` in a workflow

- `use: <path>` is a step of its own, relative to the file that contains it, like `import`. Its other keys are `with` (the params) and `name` (to replace the sequence's name in the report).
- The sequence's steps run in place of the `use` step, in order, as if they were written there:
  - they read the workflow's variables (saved values, environment, `variables`), with the params first;
  - their `save` values are visible to the next steps of the workflow, which is how a login passes its token;
  - the workflow stops at the first failing step, inside the sequence or not.
- Unknown `with` keys and missing required params are load errors, with a "did you mean", as for an endpoint.
- A sequence may `use` another one. A sequence that uses itself, directly or through others, is a load error.
- **`for_each` with `use` is not supported in this task**: it would repeat a whole group of steps per item (create, read, delete for each). It is the next step of this design, see "Out of scope".

### Reports

- Each step of a sequence is a step of the run: one line in the console, one test case in JUnit, one `step_completed` event. Its name is `<sequence name> › <step name>`.
- **Location.** A step of a sequence is reported at the `use` step in the workflow, since that is what the user is reading and where the extension draws its results. Its place in the sequence file is a separate key, `source_location`. Validation errors inside a sequence file point to that file, as errors in an imported API file do.
- The summary counts the sequence's steps as steps.

## Validation

```text
orders.yaml:6:10: sequence not found: ../sequences/log-in.yml
orders.yaml:7:7: unknown param 'pasword' for sequence 'Log in' (did you mean 'password'?)
orders.yaml:6:5: missing required param 'password' of sequence 'Log in' (defined at sequences/log-in.yaml:6:3)
orders.yaml:6:5: a use step cannot have 'call' (or 'expect', 'until', 'save', 'for_each')
orders.yaml:6:10: ../api/backend.yaml is not a sequence (it has no 'sequence:')
sequences/a.yaml:4:5: sequence 'a' uses itself: a.yaml → b.yaml → a.yaml
log-in.yaml:2:1: unknown key 'steps' in a sequence (did you mean 'sequence'?)
```

```text
$ tinyprobe run tinyprobe/sequences/log-in.yaml
Error: tinyprobe/sequences/log-in.yaml is a sequence, not a workflow: run it with `use:` from a workflow
```

## Protocol

New keys only, protocol `1`.

- `inspect --json` of a workflow: `steps` lists the steps of each sequence where its `use` is, with consecutive indexes, as the run does. Each step gets:
  - `"source_location"`: its place in the sequence file, or `null` for a step written in the workflow;
  - `"sequence"`: `{ "name": "Log in", "file": "…/log-in.yaml" }`, or `null`.

  `location` is the `use` step's, as described above.
- `inspect --json` of a sequence file: `"kind": "sequence"`, its `params` (as an endpoint's), and its `steps` with their locations in the file.
- ndjson: `run_started.steps` lists the expanded steps. `step_started` and `step_completed` are unchanged; their `name` has the prefix.
- JUnit: the test case's line is the `use` step's.

## Extension

Nothing required. The Test Explorer and the run view show the sequence's steps at the `use` line, with their prefixed names. Completion reads `use`, `sequence` and `params` from `tinyprobe schema`. No new `MINIMUM_VERSION`.

Later, in the extension (Inbox): ▶ Run workflow and CodeLens are not shown in sequence files; completion of `with` keys from the sequence's `params`; "go to definition" on a `use` path.

## Docs

- `docs/workflow-format.md`: a "Reusable sequences" section, the `use` row in the steps table, and the sequence file's keys; remove it from "Not supported yet".
- `docs/llm-guide.md`:
  - the layout gets `tinyprobe/sequences/`;
  - when to write a sequence: a login or a setup used by two workflows or more. Do not write one for a step used once;
  - every workflow still starts from zero, now with `use: ../sequences/log-in.yaml` as its first step;
  - Postman: a login request that every folder relies on becomes a sequence used by each workflow;
  - common mistakes: a workflow that relies on another workflow's login, a sequence without the `import` of the endpoints it calls, and `steps:` instead of `sequence:`.
- `README.md`: the steps table, and the format example (the login as a sequence).
- `docs/protocol.md`: `source_location`, `sequence`, `kind: sequence`.
- JSON Schema: the sequence file (`sequence`, `params`) and the `use` step.
- `tinyprobe init`: also creates `tinyprobe/sequences/`.
- `docs/example`: `docs/example/sequences/log-in.yaml`, used by the workflows that log in today (`users.yaml`, `catalog.yaml`, `tables.yaml`...).

## Out of scope

- **`for_each` over a sequence** (`use` with `for_each`): the steps of the sequence once per item, for a create/read/delete per item. It builds on this spec, needs the run view to show nested groups, and gets its own task.
- Return values declared by a sequence (`outputs:`). Its saves are visible, as for any step. If sequences start to clobber the workflow's variables, outputs can come later.
- Sequences defined inside a workflow file, or several in one file.
- Running a sequence on its own (`tinyprobe run` with `--var` for its params).

## Acceptance criteria

- [ ] Sequence files and `use` as above, with every load error at its location (inside the sequence file for its own errors).
- [ ] Unit tests:
  - params with defaults, required and unknown;
  - params local to the sequence, and saves visible after it;
  - a nested sequence, and a cycle;
  - imports shared with the workflow;
  - the workflow stops inside a sequence;
  - names with the prefix;
  - `location` and `source_location` in `inspect`, events and JUnit;
  - `run` of a folder skips sequence files, and `run` of a sequence fails with exit `2`.
- [ ] `docs/example` workflows log in through `docs/example/sequences/log-in.yaml`, in CI and in `scripts/dev.sh e2e`.
- [ ] `inspect --json` and the schema have the new keys. The extension's checks pass with the new binary.
- [ ] `scripts/dev.sh check` passes.

## Open questions

1. **A file per sequence.** The proposal writes `use: ../sequences/log-in.yaml`, a path like `import`: one obvious place to look, and each sequence can be checked on its own. The alternative is named sequences in a shared file (`sequences: { log_in: [...] }`, then `use: log_in` through `import`): shorter to write, but one more kind of name to resolve, and the file mixes several things. *Recommendation: a file per sequence.*
2. **`sequence:` as the key of the steps.** A sequence file uses `sequence:` instead of `steps:`, so `run <folder>` and the extension never take it for a workflow, and a reader sees at once what it is. The alternative is `steps:` plus `params:`: one key less to learn, but whether a file is a workflow then depends on another key. *Recommendation: `sequence:`.*
3. **Saves visible to the workflow.** The proposal makes a sequence's saves visible to the workflow, as for written steps: that is how a login hands over its token. The alternative is declared outputs (`outputs: [token]`): more explicit, but the common case gets longer. *Recommendation: visible, no outputs for now.*
4. **Where a sequence step is reported.** The proposal reports it at the `use` line of the workflow, with `source_location` for the line in the sequence file. The extension 0.5.0 then puts the results on the right line of the open workflow, and JUnit annotations point to the workflow that failed. The alternative is its line in the sequence file: more precise, but the extension today would draw it on that line number of the workflow. *Recommendation: the `use` line, plus `source_location`.*
