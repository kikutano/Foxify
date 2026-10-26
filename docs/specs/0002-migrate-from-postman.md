# 0002 — Migrating from Postman, in the guide for agents

- **Status:** Implemented
- **Roadmap:** M1-1 (Postman mapping); proposed together with M1-3 (what cannot be translated) and M1-4 (large collections); measured with M1-6 (benchmark)
- **Repositories:** tinyprobe
- **PRs:** [#14](https://github.com/kikutano/tinyprobe/pull/14)

## Problem

A Postman user who adopts tinyprobe gives their coding agent a collection export and an environment export, and asks for tinyprobe files ([decision 0001](../decisions/0001-ai-does-the-import.md)). Today `docs/llm-guide.md` (`tinyprobe guide`) teaches how to write tests by reading a backend's code. It says nothing about Postman. Its only Postman-related line is a "common mistake" about `{{$guid}}`. Without guidance, an agent will:

- copy the collection's shape: one endpoint per request, including one per variant ("Login OK", "Login wrong password");
- drop the JavaScript tests, or invent syntax for them (`expect: { status_code: 200 }`, `$.items.length`);
- put real tokens from the environment export into committed files;
- silently skip what it cannot translate (form-data, signing scripts, `setNextRequest`), so the user believes the migration is complete;
- lose requests in a large collection, with nothing to tell the user what is missing.

## Proposal

A new section of `docs/llm-guide.md`, **"12. Migrating from Postman and other tools"**, placed after "11. Complete example". The current "Checklist before you finish" becomes 13 and gets the migration items. It is written like the rest of the guide: rules, a mapping table, short examples whose YAML is checked by `DocumentationTests`, and common mistakes.

### 12.1 Procedure for a migration (M1-4)

1. **Inventory first.** Read the export files. Postman exports collection v2.1 (`info.schema` ends in `v2.1.0/collection.json`); if it is v2.0 or v1, tell the user to re-export as v2.1. List:
   - folders and requests, with their method and URL;
   - collection, folder and request `auth`;
   - collection `variable`s and the environment files with their keys;
   - `event` scripts (pre-request and test) per level.
   Write the inventory to `tinyprobe/MIGRATION.md`; it becomes the coverage report.
2. **Environments.** Each environment export (`values[]`) becomes `tinyprobe/env.<name>.yaml`. Keys of type `secret`, or whose name looks like a credential (`token`, `password`, `apiKey`, `secret`), go only into `env.local.yaml` (in `.gitignore`), with placeholders in `env.example.yaml`. Disabled values are dropped.
3. **API file.** Group requests by resource, not by folder. Merge requests that differ only in their arguments into **one endpoint** with `params`, and turn each variant into a step with different `with`.
4. **Workflows.** One workflow per folder that tests a flow, kept in the folder's request order. Each workflow logs in itself (rule of section 9), even when Postman relied on an earlier folder having set the token.
5. **Large collections.** Convert one folder at a time: `tinyprobe inspect` the files, then `tinyprobe run` the workflow, before moving to the next folder. Never convert everything and validate at the end.
6. **Coverage report.** Finish `MIGRATION.md` with a table: every request of the collection and what it became (endpoint + workflow step), or why it was skipped (12.3). Add the totals and the failures that look like backend bugs. Show the totals to the user.

### 12.2 Mapping (M1-1)

| Postman | tinyprobe |
|---|---|
| `{{var}}` in URL, headers, body | `${var}` |
| `{{baseUrl}}/users/:id`, `url.variable` | `base_url: ${baseUrl}`, `path: /users/${id}` + `params: { id: }` |
| `url.query[]` (enabled) | `query: [name, ...]`; values in `with` |
| headers, `disabled: true` | `headers`; disabled ones dropped |
| raw JSON body with `{{var}}` | a YAML mapping; a placeholder alone keeps its type (`"qty": {{qty}}` → `qty: ${qty}`) |
| `urlencoded` body | a string body `a=${a}&b=${b}` with `Content-Type: application/x-www-form-urlencoded` |
| collection / folder / request `auth` (bearer, apikey) | `auth` sets in the API file; `inherit` → the parent's set |
| `basic` auth | `Authorization: "Basic ${basicAuth}"`, with the base64 of `user:password` in the env file |
| collection `variable`, environment, globals | workflow `variables` (non-secret defaults) and env files |
| `pm.environment.set("t", pm.response.json().token)` (also `collectionVariables`, `globals`) | `save: { t: $.token }` |
| `pm.response.to.have.status(201)`, `pm.response.code === 201` | `expect.status: 201` |
| `pm.response.to.be.success` | no `status` (any 2xx) |
| `pm.expect(json.a).to.eql(1)` / `.to.equal(1)` | `$.a: 1` (`eq` for objects) |
| `.to.be.above(0)` / `.below(9)` / `.least` / `.most` | `gt` / `lt` / `gte` / `lte` |
| `.to.include(x)` (array or string) | `contains` |
| `.to.be.oneOf([a, b])` | `in: [a, b]` |
| `.to.have.property("id")`, `.to.exist` | `{ exists: true }` |
| `.to.have.lengthOf(3)` on an array | `$.items[2]: { exists: true }` and `$.items[3]: { exists: false }` |
| `pm.response.to.have.header("X-Id")` | `headers: { X-Id: { exists: true } }` |
| `pm.expect(pm.response.responseTime).to.be.below(500)` | `duration: { lt: 500ms }` |
| `{{$guid}}`, `{{$timestamp}}`, `{{$randomInt}}`, `{{$isoTimestamp}}` | `${$uuid}`, `${$timestamp}`, `${$randomInt}`, `${$now}` |
| other `{{$random...}}` (`$randomEmail`, `$randomFirstName`) | a variable built from generated values: `email: "user-${$randomString}@example.com"` |
| a retry loop (`setTimeout` + `setNextRequest` on the same request) | `until` |
| saved example responses (`response[]`) | not tests: use them to learn the response shape, then check against the real backend with `tinyprobe call` |
| `pm.test("Name", ...)` names | step `name`s |

One worked example: a small collection export (login, get, create with two variants, a test script that saves a token and checks fields), and the API file and workflow it becomes.

### 12.3 What cannot be translated (M1-3)

The rule: **never invent syntax**. When something has no tinyprobe equivalent:

1. if the request can run without it, translate the rest, and record what was dropped;
2. otherwise skip the request.

Either way, write a line in `MIGRATION.md` with the request, what was not translated and why.

| Postman feature | What to do |
|---|---|
| pre-request scripts with logic (HMAC signatures, computed hashes, timestamps in a format tinyprobe can't generate) | skip the request; report it |
| `pm.sendRequest` inside a script | turn the side request into a step before, when it is a plain request; otherwise skip |
| `postman.setNextRequest` branching (other than a retry loop) | translate the linear path; report the branches |
| data files / `pm.iterationData` | translate one iteration with the first row; report that `for_each` is not supported (ROADMAP M4-4) |
| `formdata` bodies and file uploads | skip; report (ROADMAP M4-1) |
| OAuth 2.0 auth helper | for client credentials, a token request step + `save`; other flows: report, and ask the user for a token in `env.local.yaml` |
| cookies set by one response and sent by the next | report (ROADMAP M4-2) |
| regex (`.to.match`), type checks (`.to.be.a("string")`), JSON schema (`jsonSchema`, `tv4`) | keep what the check guarantees when there is an equivalent (`exists`, `contains` for a fixed substring); otherwise drop the check and report it |
| loops over response arrays (`json.forEach`) | check the first items by index; report the rest |
| XML / HTML responses | check only `status` and headers; report |
| JSONPath beyond the subset (`[*]`, filters) | index-based checks, or report |

### Common mistakes to add (section 10)

| Mistake | Correct |
|---|---|
| one endpoint per Postman request (`login_ok`, `login_wrong_password`) | one endpoint, variants as steps with `with` |
| `{{token}}` left in a file | `${token}` |
| the environment export's real values in `env.example.yaml` | placeholders there, real values only in `env.local.yaml` |
| a workflow that expects a token saved by another workflow | each workflow logs in itself |
| a skipped request not listed anywhere | a line in `MIGRATION.md` |

## Validation

No new errors. The YAML examples of the new section are loaded by `DocumentationTests`, like the rest of the guide (blocks marked `# file:` are written and imported together).

## Protocol

No change.

## Extension

No change. M1-9 ("Migrate with AI" command) will use this section.

## Docs

- `docs/llm-guide.md`: sections 12 and 13, the new common mistakes.
- README: nothing in this spec. The ready-made Postman prompt is M1-5.

## Measuring it (M1-6)

The point of the section is better conversions, so we measure them before and after.

- **Fixture:** `docs/example/postman/`, a Postman v2.1 collection and one environment for the test server. It covers:
  - variables and auth inheritance;
  - a login that sets the token from a test script;
  - variants of the same request;
  - assertions of every kind in the mapping table;
  - one urlencoded body;
  - and, on purpose, cases from 12.3: a signing pre-request script, a form-data upload, a regex check.
- **Score** for one conversion, done by an agent that only gets the export files and the guide:
  - rounds of `inspect` / `run` until everything passes;
  - requests covered, out of the total;
  - untranslatable cases reported, out of the planted ones;
  - invented syntax (keys or operators that don't exist);
  - secrets in committed files.
- **Runs:** with the current guide (baseline), then with the new section. Claude Code runs both; a second agent is added when the user runs one by hand. Scores are recorded at the end of this spec.

## Out of scope

- Other sources (OpenAPI, Insomnia, Bruno, HAR, curl): M1-2, a short subsection each in section 12 after this one lands.
- The README prompt (M1-5), `tinyprobe init` (M1-8) and the extension command (M1-9).
- Any change to the format: features that are missing (form-data, cookies, `for_each`) stay in M4 and are only reported.

## Acceptance criteria

- [x] Sections 12 and 13 and the new common mistakes are in `docs/llm-guide.md`; `tinyprobe guide` prints them.
- [x] Every YAML example in the guide loads (`DocumentationTests`).
- [x] The Postman fixture is in `docs/example/postman/`, with a README that says how to score a conversion. It is a test bench for the guide, not something users get: no reference conversion, no CI step.
- [x] Baseline and new scores are recorded here. With the new section:
  - no invented syntax and no secrets in committed files;
  - every planted untranslatable case is reported;
  - every translatable request is covered.
- [x] `scripts/dev.sh check` passes.

## Decisions

Answered on 2026-10-25:

1. M1-1, M1-3 and M1-4 are one spec and one PR. M1-2 (other sources) follows on its own.
2. One guide file: `tinyprobe guide` prints everything.
3. Benchmark first, so the baseline uses the current guide. Claude Code runs non-interactively (`claude -p`) in a temporary copy; a second agent can be run by hand with the same prompt.
4. The coverage report is `tinyprobe/MIGRATION.md`, kept in the user's repository.
5. The Postman collection is only a test bench for the guide. The worked example inside the guide teaches agents and is already checked by `DocumentationTests`, so there is no reference conversion to maintain.

## Scores

On the bench of `docs/example/postman/` (20 requests: 18 translatable, 2 not; 44 assertions; 4 planted untranslatable cases). "Rounds" counts `tinyprobe` invocations, `guide` excluded.

| Run | Guide | Agent | Rounds | Invalid files | Requests covered | Checks kept | Untranslatable reported | Secrets committed | Coverage report |
|---|---|---|---|---|---|---|---|---|---|
| baseline | 0.10.0 | Claude Code, Sonnet | 8 | 0 | 18 / 18 | 40 / 44 (2 approximated) | 4 / 4, in the answer only | 0 | no |
| baseline | 0.10.0 | Claude Code, Haiku | 26 | 0 | 18 / 18 | 40 / 44 (2 approximated) | 4 / 4, in the answer only | 0 | no |
| new | section 12 | Claude Code, Sonnet | 7 | 0 (one `call` without a required argument) | 18 / 18 | 40 / 44 (2 approximated) | 4 / 4, in `MIGRATION.md` | 0 | yes, every request |
| new | section 12 | Claude Code, Haiku | 26 | 0 | 18 / 18 | 40 / 44 (2 approximated) | 4 / 4, in `MIGRATION.md` | 0 | yes, every request |

Baseline notes: variants were merged into one endpoint (`login` with a wrong password, `bearer_check` with an invalid token). The regex became `contains "@example.com"`, and `forEach` became a check of `$.data[0]`. Nothing was written down: the list of what was not converted exists only in the final chat answer, and is lost when the session ends.

With section 12, both models followed the procedure. Sonnet wrote `MIGRATION.md` with all 20 requests, the 2 skipped ones and the 2 weakened checks, and the totals. It also listed the Postman variables it did not carry over and why. The other measures stayed at their best. Haiku did the same, and also counted the dropped and the weakened checks separately.

What this bench shows: the current guide already makes both models convert well, so section 12 mainly adds what was missing, a written coverage report. It did not cost extra rounds. This collection is small and clean; a large or messy one (hundreds of requests, nested folders, heavy scripts) would test the inventory and the one-folder-at-a-time rule, and is a good next bench.
