# Postman test bench

A Postman collection and environment for the tinyprobe test server, written the way a Postman user would: a collection-level bearer token set by a login script, variants of the same request, tests in JavaScript, a computed email, an urlencoded body. Newman runs it green against the test server (20 requests, 44 assertions).

This folder is **not** an example for users. It measures how well a coding agent migrates a Postman collection by following `tinyprobe guide` ([spec 0002](../../specs/0002-migrate-from-postman.md), ROADMAP M1-6). Change the guide, run the bench again, compare the scores.

## What a good migration does

| | Requests | What the agent should do |
|---|---|---|
| Translatable | 18 | Convert them. Variants become steps of one endpoint: "Login - wrong password", "Bearer check - invalid token", "Create user - missing email", "List active users". |
| Untranslatable request | 2 | "Signed echo" (HMAC signature in a pre-request script) and "Upload an avatar" (form-data with a file): skip or translate partially, and report why. |
| Untranslatable check | 2 | The regex in "Get user" (`.to.match`) and the `forEach` in "List active users": drop or approximate them, and report it. |

`password` and `apiKey` are `secret` values in the environment: they must not end up in a file that would be committed.

## Run it

1. Start the test server: `scripts/dev.sh server`.
2. Copy the two JSON files into an empty folder, `postman/` inside a new git repository, with `tinyprobe` on the `PATH` (the version whose guide you are measuring).
3. Give the agent this prompt, and nothing else:

   ```text
   This repository's API is tested with Postman: the collection and the environment are in postman/.
   Migrate them to tinyprobe. Read the guide with `tinyprobe guide` and follow it.
   The backend is running at http://localhost:5122.
   When the workflows pass, tell me what you converted and what you could not convert.
   ```

## Score

| Measure | How |
|---|---|
| Rounds | `tinyprobe inspect` / `run` / `call` invocations until the last run passes. |
| Invalid files | Invocations that exited with `2` (the YAML was rejected), and syntax that does not exist left in the final files. |
| Requests covered | Of the 18 translatable requests, how many have a step in a workflow that passes. |
| Checks kept | Of the 44 assertions, how many have an equivalent check. |
| Untranslatable reported | Of the 4 planted cases (2 requests, 2 checks), how many the agent reports in its answer or in a file. |
| Secrets committed | Files outside `.gitignore` that contain the `password` or `apiKey` values. |

Record each run in the "Scores" table of spec 0002.
