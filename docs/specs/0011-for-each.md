# 0011 — `for_each`: repeat a step over a list

- **Status:** Implemented
- **Roadmap:** M4-4
- **Repositories:** tinyprobe
- **PRs:** [#27](https://github.com/kikutano/tinyprobe/pull/27)

## Problem

The same request often has to be checked with several inputs:

- validation rules: an empty email, a too-long name and a negative quantity, each with its `400` or `422` and its error field;
- every role against a protected endpoint;
- every item of a list the API just returned.

Today each case is a copied step. Ten cases are ten steps that differ in one line, and adding a case means copying a block. Postman users do this with data files (`newman run -d users.csv`, `pm.iterationData`). The guide tells agents to translate the first row only and to report that `for_each` is not supported.

## Proposal

```yaml
- name: Invalid users are rejected
  call: create_user
  for_each:                             # one call per item
    - { email: "", error: email_required }
    - { email: not-an-email, error: email_invalid }
    - { email: "${$randomString}@example.com", firstName: "", error: first_name_required }
  with:
    firstName: Test                     # the same for every item, unless the item sets it
  expect:
    status: 422
    body:
      $.code: ${error}                  # item values are variables of the step
```

```text
  ✓ Invalid users are rejected [1/3]   POST   422    4.10ms  /api/users
  ✗ Invalid users are rejected [2/3]   POST   201    3.87ms  /api/users
      ✗ $.code == "email_invalid"  actual (missing)
  ✓ Invalid users are rejected [3/3]   POST   422    3.52ms  /api/users
```

The list can also come from a variable or from a file:

```yaml
- name: Every listed user can be read
  call: get_user                        # its param `id` comes from each item
  for_each: ${users}                    # saved by an earlier step: save: { users: $.data }
  expect:
    status: 200

- name: Roles
  call: admin_stats
  for_each: { file: ../data/roles.csv } # role,status / admin,200 / viewer,403
  expect:
    status: ${status}
```

### Items

- `for_each` is a list of mappings. Each mapping is one iteration, and its keys are **arguments of the call**, as in `with`. An item can set an argument that `with` does not set. `with` holds the arguments that are the same for every item, and may use the item's values (`email: "${name}@example.com"`).
- An item's keys are also **variables of the step**, so `expect` and `until` can use them. A key that is neither an argument of the endpoint nor used by the step is an error, with a "did you mean" (the same rule as `with`).
- A key in both an item and `with` is an error: `item 2 of for_each and with both set 'firstName': keep one`.
- **Literal list.** Items are validated when the workflow loads.
- **`${name}`.** The value of a variable at run time: from an earlier `save`, `variables` or the environment. It must be a list of objects, or the step fails with `for_each: 'users' is a string, not a list of objects`. Keys that the endpoint does not use are ignored, since an API response has fields the call does not need.
- **`{ file: path }`.** A data file, relative to the workflow, like `import` and multipart files:
  - `.json`: a list of objects;
  - `.csv`: the first line is the header, each other line is an item. Values are text (`200` and `"200"` compare the same in `status`, see below); empty lines are skipped.
  - The file is read when the workflow loads, so its keys are validated like a literal list. A missing file, or one that is not a list of objects, is a load error.
- An empty list runs nothing, and the step counts as passed with no iterations.

### Running

- Iterations run **in order, one after the other**. Each one is a whole call: `until` and `max_retries` apply to each one.
- **Every iteration runs, also after a failing one**, so a table of cases shows every case that fails. Then, if any failed, the step has failed and the workflow stops, as for any step.
- `expect.status` may be a placeholder in a `for_each` step (`status: ${status}`), resolved for each iteration from the item. Its value must be a status code or a list of them. This is the one place where `status` takes a placeholder; elsewhere it stays a literal.
- `save` is not allowed in a `for_each` step: every iteration would overwrite the value. See "Out of scope".
- Generated values (`${$uuid}`) are new for each iteration.

### Names and reports

- **The console** prints one line per iteration. The step's name is followed by `[2/3]`, and the default name (the call plus its path and query arguments) is computed for each item: `get_user id=7 [2/3]`.
- **The summary** counts each iteration as a step (`steps: 9/9 passed`).
- **JUnit** has one test case per iteration, with the same name, so CI shows each case.

## Validation

```text
users.yaml:14:7: for_each must be a list of items, a variable (${cases}) or { file: path }
users.yaml:15:9: item 2 of for_each must be a mapping of arguments ({ email: x })
users.yaml:16:11: unknown argument 'emial' for endpoint 'create_user' (did you mean 'email'?)
users.yaml:17:11: item 3 of for_each and with both set 'firstName': keep one
users.yaml:20:5: save cannot be used with for_each: every iteration would overwrite the value
users.yaml:14:17: cannot read the for_each file ../data/roles.csv: not found
users.yaml:14:17: ../data/roles.json must be a list of objects
users.yaml:22:15: status must be a code, or a placeholder in a for_each step
```

When the step runs:

```text
for_each: 'users' is a string, not a list of objects
Expected status 422, got 201
Status '${status}' of item 2 is not a status code: 'ok'
```

## Protocol

New keys only, protocol `1`.

- `inspect --json`, each call step: `"for_each"` is `null`, or one of:
  - `{ "items": [ { "email": "" }, … ] }`, with literal values; a file's items are inlined, with the file path in `"file"`;
  - `{ "variable": "users" }`.

  `expect.status` keeps being a list of codes; a placeholder status gives an empty list plus `"status_placeholder": "${status}"`.
- ndjson:
  - `step_started` gets `"iteration"` and `"iterations"` (1-based), on every iteration.
  - **`step_completed` is sent once per step**, when its last iteration ends, with a new key `"iterations"`: one object per iteration, with the same fields as a step (`success`, `failure`, `method`, `url`, `status`, `duration_ms`, `attempts`, `assertions`, `request`, `response`), plus `"iteration"`.
  - The step's own fields describe the first failing iteration, or the last one when all passed. A tool that does not know `iterations` (the extension today) therefore shows the right result, and the details of the iteration to look at.
  - `"iterations"` is `null` for a step without `for_each`.
- `run_finished` counts iterations as steps.
- The MCP `run` tool returns the same JSON.

## Extension

Nothing required: the run view and the Test Explorer read the step's own fields, which are the failing iteration's. Showing every iteration in the run view, as a list under the step, is a later extension task (Inbox). Completion reads `for_each` from `tinyprobe schema`. No new `MINIMUM_VERSION`.

## Docs

- `docs/workflow-format.md`: a "Repeating a step: `for_each`" section, and the row in the steps table; remove it from "Not supported yet".
- `docs/llm-guide.md`:
  - when to use `for_each` (a table of validation cases, roles, every item of a list) and when not (cases that need different endpoints or a `save`);
  - Postman data files and `pm.iterationData` become `for_each: { file: … }`, with the file copied to `tinyprobe/data/`;
  - the common mistake: ten copied steps that differ in one value.
- `README.md`: the steps table.
- `docs/protocol.md`: `for_each` in `inspect`, `iteration` and `iterations` in the events.
- JSON Schema: `for_each` with a description.
- `docs/example`: a `for_each` step for each form (literal, variable, CSV file) in `users.yaml` or a new `validation.yaml`, against the test server's validation errors, with the CSV in `docs/example/data/`.

## Out of scope

- `save` in a `for_each` step, for example to collect the ids of created resources and delete them later. It could become a list, one value per iteration. A later task, if workflows need it.
- Running iterations in parallel.
- A `for_each` over a group of steps (create, read, delete for each item). That needs the reusable step sequences of M4-5.
- Combinations of several lists (a matrix).
- Placeholders in other places that take literals today (`until.every`, `sleep`).

## Acceptance criteria

- [x] `for_each` with a literal list, a variable and a JSON or CSV file, as above, with every load and run error.
- [x] Unit tests with `FakeServer`:
  - arguments from items and `with`;
  - item values in checks, and `status: ${status}`;
  - all iterations run after a failure, then the workflow stops;
  - `until` per iteration;
  - the variable form, with extra keys ignored, and a wrong type;
  - CSV and JSON files, an empty list;
  - names and the summary counts.
- [x] ndjson: `iteration` and `iterations` in `step_started`; one `step_completed` with `iterations`, whose own fields are the first failing iteration's. JUnit has one test case per iteration.
- [x] `inspect --json` and the schema have `for_each`.
- [x] The `docs/example` steps pass against the test server, in CI and in `scripts/dev.sh e2e`.
- [x] `scripts/dev.sh check` passes.

## Open questions

1. **After a failing iteration.** The proposal runs every iteration, then stops the workflow if any failed: a table of cases is read like unit tests, and each failure is useful. The alternative is to stop at the first failing iteration, as a workflow stops at its first failing step. *Recommendation: run every iteration.*
2. **Events.** The proposal sends one `step_completed` per step, with an `iterations` array, and the failing iteration's fields at the top: the extension 0.5.0 keeps working and shows the right result. The alternative is one `step_completed` per iteration. That is simpler, but the extension keys steps by index, so it would show only the last iteration, and a failure followed by a pass would look green. *Recommendation: one event, with `iterations`.*
3. **Data files in this task.** `{ file: … }` with JSON and CSV makes Postman's data files (`-d users.csv`) translate one to one. Without it, an agent copies the rows into a literal list, which works for small files. *Recommendation: include JSON and CSV.*
4. **`status: ${status}`.** Validation tables often expect a different status per case (`201` for a valid one, `422` for the others). Without it, those need two `for_each` steps, split by status. *Recommendation: allow it, only in `for_each` steps.*

## Decisions

Approved on 2026-10-28 with the recommendations:

1. Every item runs, also after a failing one; then the workflow stops if any failed.
2. One `step_completed` per step, with `iterations`, and the first failing item's fields at the top.
3. Data files (`{ file: … }`, JSON and CSV) are part of this task.
4. `expect.status` may be a placeholder in a `for_each` step only.
