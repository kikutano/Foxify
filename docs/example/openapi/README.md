# OpenAPI test bench

An OpenAPI 3 document for the authentication and user endpoints of the tinyprobe test server, written the way a backend team would: a global bearer scheme, an API key scheme on one operation, `$ref` and `allOf` schemas, an `example` with a user that does not exist, and credentials in the description.

This folder is **not** an example for users. It measures how well a coding agent turns an OpenAPI document into tinyprobe files by following `tinyprobe guide` (section 12.5, ROADMAP M1-2), like the [Postman bench](../postman/README.md).

## What a good conversion does

| | Operations | What the agent should do |
|---|---|---|
| Translatable | 11 | An endpoint each, called by a passing workflow: `getPublic`, `login`, `checkBearer`, `checkApiKey`, `listUsers`, `createUser`, `getUser`, `replaceUser`, `updateUser`, `deleteUser`, and `uploadAvatar` (`multipart/form-data`, since 0.17.0: a `multipart` endpoint and a small file in `tinyprobe/fixtures/`). |
| Document and backend differ | 1 | `createUser` with an empty email answers `400`, the document says `422`: report the difference. |
| Example values | — | The `User` example (id `42`, Jane Smith) is not in the seed data: checks must use real values, found with `tinyprobe call`. |

The password and the API key must not end up in a file that would be committed.

## Run it

1. Start the test server: `scripts/dev.sh server`.
2. Copy `openapi.yaml` into an empty git repository, with `tinyprobe` on the `PATH` (the version whose guide you are measuring).
3. Give the agent this prompt, and nothing else:

   ```text
   Read the guide with `tinyprobe guide` and follow it, starting from the OpenAPI document openapi.yaml:
   write one tinyprobe endpoint per operation and workflows that check the documented status codes and
   response fields, then run them against the local backend until they pass. The backend is running at
   http://localhost:5122. Take test values from the seed data, not from the examples in the document.
   Put credentials only in tinyprobe/env.local.yaml and add it to .gitignore. Tell me where the backend
   does not do what the document says.
   ```

## Score

| Measure | How |
|---|---|
| Rounds | `tinyprobe inspect` / `run` / `call` invocations until the last run passes. |
| Invalid files | Invocations that exited with `2`, and syntax that does not exist left in the final files. |
| Operations covered | Of the translatable operations, how many have a step in a workflow that passes. |
| `uploadAvatar` | Sent as `multipart` with a file, and passing (`204`). Before 0.17.0: reported as untranslatable. |
| Difference reported | Whether the `400` / `422` difference of `createUser` is reported. |
| Example values | Checks that expect a value of the document's examples. |
| Secrets committed | Files outside `.gitignore` that contain the password or the API key. |

## Scores

Claude Code, Sonnet. The runs of 2026-10-26 were made before the test server had an avatar route: a conversion that sent the upload anyway got `404`, and 10 operations were translatable.

| Guide | Rounds | Exit `2` | Operations covered | `uploadAvatar` | `400` / `422` | Example values | Secrets committed | Coverage report |
|---|---|---|---|---|---|---|---|---|
| 0.11.0 | 21 | 0 | 10/10 | sent as a hand-built multipart string, reported as a failing workflow | reported, check kept | 0 | 0 | no |
| 12.5, first draft | 15 | 1 | 10/10 | skipped, reported in the answer | reported, check kept | 0 | 0 | no |
| 12.5 | 26 | 1 | 10/10 | skipped, reported in `MIGRATION.md` | reported in `MIGRATION.md`, check kept | 0 | 0 | `MIGRATION.md`, a row per operation |

The guide of 0.11.0 already converts the document well. Section 12.5 adds the rule for multipart bodies (skip and report instead of building the body by hand) and the coverage report. The exit `2` of both runs is a `tinyprobe call` of `login` without its required arguments. The number of rounds varies between runs of the same guide, so one run per guide is not enough to compare them.
