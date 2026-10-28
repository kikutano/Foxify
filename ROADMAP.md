# tinyprobe roadmap

The plan for **both repositories**: this one (the `tinyprobe` CLI, its format and the guide for coding agents) and [tinyprobe-vscode](https://github.com/kikutano/tinyprobe-vscode) (the VS Code extension). Every development session starts here.

## Principles

1. **The user's AI does the import.** tinyprobe ships no AI model and no direct importers. Users give their own coding agent (Claude Code, Cursor, Copilot, Codex, ...) their Postman collections, OpenAPI documents or backend code. tinyprobe provides what makes any agent good at it: the guide (`tinyprobe guide`), validation with precise errors, and the `inspect` / `call` / `run` loop that lets the agent check its own work. See [decision 0001](docs/decisions/0001-ai-does-the-import.md).
2. **One engine: the binary.** The extension and every other tool drive `tinyprobe` through its [JSON protocol](docs/protocol.md) and never reimplement parsing or checks.
3. **Plain text, Git and CI first.** Endpoints, workflows and environments are YAML files that live in the user's repository and run unchanged in CI.
4. **Endpoints declare, workflows run.** An API file only describes requests: no checks, no `save`, no state. Every value passed between requests is saved by a step of a workflow, and a single `call` remembers nothing. See [decision 0003](docs/decisions/0003-endpoints-declare-workflows-run.md).

## Where we are

Released: **tinyprobe 0.17.0** (forms and file uploads; Docker image `ghcr.io/kikutano/tinyprobe`) and the extension **0.5.0**. M1, M2 and M3 are done; M4 is in progress.

- Format: API files with endpoints, params, query, headers, auth sets and bodies. Workflows import them and run `call` / `with` / `expect` / `save` steps, with `sleep`, `until` (polling), generated values (`${$uuid}`, `${$now+2h}`...) and AND/OR checks. Full validation before running, with `file:line:column` and "did you mean".
- CLI: `run` (one workflow, or a folder as a suite, with a JUnit report), `call`, `inspect`, `init`, `mcp` (an MCP server for agents), `schema` (JSON Schema of the format), `update`, `guide`; console and ndjson reporters; a GitHub Action (`kikutano/tinyprobe@vX.Y.Z`); Native AOT binaries for win-x64, linux-x64, linux-arm64 and osx-arm64; install scripts.
- Extension: validation on save, completion and hover, ▶ Call, ▶ Run workflow, Test Explorer, run view, environment picker, Migrate with AI, Generate Tests with AI, the MCP tools in VS Code's chat. On the VS Code Marketplace, on Open VSX (namespace claim pending) and as a `.vsix` in GitHub Releases.

## How we work

- **Status:** `[ ]` todo · `[~]` in progress · `[x]` done · `[-]` dropped (reason in the task) · `[?]` needs a decision.
- **Areas:** `cli` this repository's code · `guide` `docs/llm-guide.md` and other docs for agents · `ext` tinyprobe-vscode · `client` the standalone client (M5) · `repo` project housekeeping.
- **Specs before code** for every task that changes the YAML format, the CLI surface (commands, options, output) or the protocol. Use `/spec`: it writes `docs/specs/NNNN-name.md` from the [template](docs/specs/0000-template.md). Code starts when the spec is *Approved*; then `/add-feature`. Other tasks need no spec.
- **Decisions** that constrain future work go in `docs/decisions/` as short records: context, decision, consequences.
- **Done** means the acceptance criteria are met and `scripts/dev.sh check` passes in every repository touched. The task is marked `[x]` in the same PR, with a link to the PR.
- **New ideas** go to the [Inbox](#inbox). We move them into a milestone when we plan the next one.
- Milestones are in order, and tasks inside one are roughly in order. There are no dates.

---

## M0 — Restart

Clean up the project before inviting people in.

- [x] **M0-1** `repo` Replace the old roadmap: this file, `docs/specs/`, `docs/decisions/`, the `/spec` skill. `Milestone1.md` removed: it planned the pre-0.2 format (the old roadmap is in git history: `git show 0e2981c:ROADMAP.md`). Commit 41144ac.
- [x] **M0-2** `repo` Development tooling in both repositories: `scripts/dev.sh`, `CLAUDE.md`, the `/add-feature`, `/release` and `/spec` skills. Commits 05f6f55, and 45bcb6c in tinyprobe-vscode.
- [x] **M0-3** `repo` Remove `Modelfile` (a personal Ollama configuration) from the repository.
- [x] **M0-4** `repo` README: fix the opening sentence ("Tired of paying for Postman?"), refresh the sample output (it still shows 0.4.0), link this roadmap.
- [x] **M0-5** `repo` Choose a name that is free for a developer tool (GitHub, npm, VS Code Marketplace, Open VSX, trademarks): **tinyprobe**, [decision 0002](docs/decisions/0002-name-tinyprobe.md).
- [x] **M0-6** `repo` `CONTRIBUTING.md` (how to build, test and propose a feature with a spec), issue and PR templates, in both repositories.
- [x] **M0-7** `ext` Publish on the VS Code Marketplace and Open VSX. Open VSX is published by the release workflow (`OVSX_PAT`); the Marketplace gets each `.vsix` by hand on its publisher page, because Azure DevOps retires the tokens it accepted on 2026-12-01 (see the Inbox).
- [x] **M0-8** `repo` Use the name tinyprobe everywhere, in both repositories: binary, install scripts, release assets, namespaces, docs, the extension id, commands and settings, the GitHub repositories. PRs #13, and #1 in tinyprobe-vscode.
- [x] **M0-9** `repo` A logo and an extension icon for tinyprobe: a shell prompt followed by a check mark (`media/` in both repositories). The social preview image `media/social-preview.png` is uploaded by hand in the repository settings.

## M1 — Migrate with your AI

The adoption hook: a Postman user gives their collection to their agent and gets tinyprobe workflows that pass.

- [x] **M1-1** `guide` Migration from Postman: a section of `docs/llm-guide.md` mapping collection v2.1 and environments to tinyprobe. Covers `{{var}}` → `${var}`, environments and globals → env file, collection and folder auth → `auth` sets, folders → workflows, `pm.test` / `pm.expect` → `expect`, `pm.environment.set` from the response → `save`, dynamic variables → generated values, retry loops → `until`. It also tells the agent to merge request variants into one endpoint used with different `with`. Spec: [0002](docs/specs/0002-migrate-from-postman.md). PR #14.
- [x] **M1-2** `guide` Other sources, a short mapping each: OpenAPI 3 / Swagger 2, Insomnia, Bruno, HAR, `curl` commands. Measured on an OpenAPI document of the test server (`docs/example/openapi/`). PR #15.
- [x] **M1-3** `guide` What cannot be translated, and the rule: never invent syntax; skip the request and list it in the report with the reason. Covers pre-request scripts with logic (signatures, computed values), `setNextRequest`, form-data and files, OAuth2 flows, cookies, non-JSON checks, unsupported JSONPath. PR #14.
- [x] **M1-4** `guide` Large collections: inventory first (requests, folders, environments), convert one folder at a time, finish with a coverage report (requests → endpoints, workflows, skipped with reasons). PR #14.
- [x] **M1-5** `repo` Ready-to-paste prompts in the README: migrate a Postman collection, start from an OpenAPI document, scan a backend (exists, review it).
- [x] **M1-6** `guide` Benchmark for the guide: a Postman collection of the test server with variables, scripts and variants (`docs/example/postman/`), a reference conversion, and a procedure to score an agent's conversion: rounds of `inspect` / `run` until it passes, requests covered, invented syntax. Run with at least two agents before and after M1-1…M1-4. *Done when:* the scores are recorded in the spec of M1-1. Run with Claude Code (Sonnet and Haiku); other agents can be added by hand. PR #14.
- [x] **M1-7** `cli` `tinyprobe run <folder>`: runs every workflow of a folder, one summary, exit code `1` if any failed. Agents and CI run a whole migration with one command. Spec: [0003](docs/specs/0003-run-a-folder.md). PR #16.
- [x] **M1-8** `cli` `tinyprobe init`: creates the `tinyprobe/` layout of the guide (`api/`, `workflows/`, `env.example.yaml`), the `.gitignore` entries, and a short `AGENTS.md` section that points agents to `tinyprobe guide`. Spec: [0004](docs/specs/0004-init.md). PR #17.
- [x] **M1-9** `ext` "tinyprobe: Migrate with AI": pick a collection or an OpenAPI file, build the migration prompt, and hand it to the editor's chat, or copy it to the clipboard when there is no chat. tinyprobe-vscode PR #2.

## M2 — Agents everywhere

Make tinyprobe a tool that agents use directly, not only a format they write.

- [x] **M2-1** `cli` `tinyprobe mcp`: an MCP server over stdio with the tools `guide`, `inspect`, `call` and `run`, returning the protocol JSON. Native AOT-safe. Spec: [0005](docs/specs/0005-mcp-server.md) PR #18.
- [x] **M2-2** `guide` Ready-made agent packages: a Claude Code skill, Cursor rules, Copilot instructions and `llms.txt`, each a thin wrapper around `tinyprobe guide`. In `agents/` and `llms.txt`; the Claude Code plugin installs from this repository as a marketplace. PR #19.
- [x] **M2-3** `ext` Completion and hover in tinyprobe files: keys, endpoint names after `call:`, params in `with`, from `inspect --json`. Spec: [0006](docs/specs/0006-completion-and-hover.md) PR #20, tinyprobe-vscode PR #5.
- [x] **M2-4** `ext` "tinyprobe: Generate Tests with AI": runs on the model the user already has (Copilot or another provider), with the guide as instructions and `inspect` / `run` to check the result. Built on the editor's own agent instead of a loop of ours on the Language Model API: the command opens the chat in agent mode with the prompt for the backend's code, and the agent uses the tinyprobe MCP tools that M2-5 registers. tinyprobe-vscode PR #4.
- [x] **M2-5** `ext` Register the tinyprobe MCP server with VS Code (`vscode.lm.registerMcpServerDefinitionProvider`), so Copilot and other chat agents in the editor get the tools without configuration. Needs M2-1. tinyprobe-vscode PR #3.

## M3 — CI

- [x] **M3-1** `cli` JUnit XML report, read by every CI server. Spec: [0007](docs/specs/0007-junit-report.md). PR #21.
- [x] **M3-2** `cli` An official GitHub Action: installs a pinned `tinyprobe` and runs a folder of workflows, with a job summary. In this repository (`action.yml`): `uses: kikutano/tinyprobe@v<version>` installs the binary of the same release. PR #22.
- [x] **M3-3** `cli` A small Docker image: `ghcr.io/kikutano/tinyprobe`, the release's Alpine (musl) binary on `runtime-deps` Alpine, amd64 and arm64, pushed by the Release workflow. Alpine binaries are release assets too (`install.sh` and `tinyprobe update` pick them on Alpine). PR #23.
- [x] **M3-4** `cli` Failing steps as GitHub Actions annotations, at the step's line. Done by the GitHub Action, from the JUnit report (`annotations` input). PR #22.

## M4 — What Postman users miss

- [x] **M4-1** `cli` `multipart/form-data` bodies and file uploads, and `application/x-www-form-urlencoded` forms. Spec: [0008](docs/specs/0008-form-and-multipart-bodies.md). PR #24.
- [x] **M4-2** `cli` Cookies: a cookie jar per run, `expect.cookies`, `save` from a cookie. Spec: [0009](docs/specs/0009-cookies.md). PR #25.
- [x] **M4-3** `cli` Basic auth and OAuth2 client credentials: `basic` and `oauth2` in auth sets. Spec: [0010](docs/specs/0010-basic-and-oauth2.md). PR #26.
- [x] **M4-4** `cli` `for_each`: repeat a step over a list of items (written, a variable, or a CSV or JSON file). Spec: [0011](docs/specs/0011-for-each.md). PR #27.
- [x] **M4-5** `cli` Reusable step sequences (a shared "log in"): sequence files run with `use`. Spec: [0012](docs/specs/0012-reusable-sequences.md). PR #28.
- [ ] **M4-6** `cli` Namespaced imports (`call: server.login`). *Spec needed.*
- [ ] **M4-7** `ext` Request history: the last calls with their responses, to send again.
- [ ] **M4-8** `ext` Environment editor: see and edit the variables of the selected environment.
- [ ] **M4-9** `ext` Visual request editor for people who prefer forms to YAML; it writes the YAML.
- [ ] **M4-10** `cli` `ext` Run to here: `tinyprobe run <workflow> --to <step>` runs the workflow up to that step and stops; in the extension, a ▶ Run to here on every step shows that step's response. Tries a flow one request at a time without state between single calls ([decision 0003](docs/decisions/0003-endpoints-declare-workflows-run.md)). *Spec needed.*
- [x] **M4-11** `repo` A performance guard in CI: the Native AOT binary must stay under a size budget, and `--version`, `inspect` and a run of many fast requests under a time budget (measured on 2026-10-28: 9.09 MB, 13 ms to start, 0.6 ms per step against the local test server). A PR that goes over fails, so a slower or heavier tinyprobe is a choice and never an accident. `scripts/perf.sh`, in the CI job and as `scripts/dev.sh perf`; budgets 10 MiB, 50 ms, 500 ms for `inspect` of 500 steps, 2 s for a run of them. PR #29.

## M5 — A client outside the editor

A Postman-like app for people who don't work in VS Code, as light as the binary: no account, no cloud, the files of the repository.

- [ ] **M5-1** `client` A standalone client in plain JavaScript on Node, with **no dependencies**: `node` serves a small local web UI and opens it in the browser. It lists the API files and workflows of a folder, calls an endpoint from a form (params, query, body), runs workflows with live results, and picks the environment. Like the extension, it has no engine of its own: it runs the `tinyprobe` binary and reads its [JSON protocol](docs/protocol.md), and it reuses the run view of the extension (`media/run-view.js`, which already renders ndjson events in any browser). *Spec needed:* where it lives (a third repository or a folder), how it is installed (`npx`, a single file), and what it shows first.
- [ ] **M5-2** `client` Request history and an environment editor in the client, shared with the extension tasks M4-7 and M4-8 where the code can be the same.
- [ ] **M5-3** `client` A self-explanatory picture of the flows being run: the steps of a workflow as a diagram (what each step calls, the values it saves and the steps that use them, `until` loops, `for_each` iterations), lit up live as the run goes. Someone who opens a workflow for the first time understands it at a glance. To explore in the spec of M5-1, when the client is designed.

## M6 — Tell the world

- [ ] **M6-1** `repo` Rewrite `README.md` around the killer features, after M4 and M5: the migration done by the user's own AI and made reliable by the guide, the validation and the `inspect` / `call` / `run` loop (with the bench numbers); tests without hidden state (API files declare, workflows run); CI first (one binary, Docker image, GitHub Action, JUnit); one engine for the CLI, the extension, MCP and the client; no account and no cloud. Lead with what makes tinyprobe different, then install and quick start; move the reference material to `docs/`.

## Parked

Not planned now. We reconsider them after M4.

- Load testing: N concurrent copies of a workflow, with throughput and latency percentiles.
- A cloud service: shared workspaces, run history, dashboards.

## Dropped

- [-] Direct importers (`tinyprobe import postman` and similar), and an AI model or provider inside tinyprobe. The user's agent does both better; see [decision 0001](docs/decisions/0001-ai-does-the-import.md).

## Inbox

New ideas, not planned yet.

- `cli` Faster loading of large workflows: loading and validating cost about 0.2 ms per step (450 ms for 2000 steps, the same in 0.17.0), likely the arguments of every step being recomputed from the endpoint's templates. Profile before changing it.
- `cli` `for_each` over a sequence (`use` with `for_each`): the steps of the sequence once per item, for a create/read/delete per item. Builds on spec 0012.
- `ext` Sequence files: no ▶ Run workflow or Test Explorer entry for them, completion of a `use` step's `with` from the sequence's `params`, and "go to definition" on a `use` path.
- `ext` Show the items of a `for_each` step in the run view (the `iterations` of `step_completed`), each with its request and response, and in the Test Explorer.

- `ext` Publish to the VS Code Marketplace from the release workflow, without a token: Trusted Publishing when Microsoft ships it, or Microsoft Entra ID with a GitHub federated credential. Until then each `.vsix` is uploaded by hand.
