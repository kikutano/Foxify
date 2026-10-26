# tinyprobe roadmap

The plan for **both repositories**: this one (the `tinyprobe` CLI, its format and the guide for coding agents) and [tinyprobe-vscode](https://github.com/kikutano/tinyprobe-vscode) (the VS Code extension). Every development session starts here.

## Principles

1. **The user's AI does the import.** tinyprobe ships no AI model and no direct importers. Users give their own coding agent (Claude Code, Cursor, Copilot, Codex, ...) their Postman collections, OpenAPI documents or backend code. tinyprobe provides what makes any agent good at it: the guide (`tinyprobe guide`), validation with precise errors, and the `inspect` / `call` / `run` loop that lets the agent check its own work. See [decision 0001](docs/decisions/0001-ai-does-the-import.md).
2. **One engine: the binary.** The extension and every other tool drive `tinyprobe` through its [JSON protocol](docs/protocol.md) and never reimplement parsing or checks.
3. **Plain text, Git and CI first.** Endpoints, workflows and environments are YAML files that live in the user's repository and run unchanged in CI.

## Where we are

Released: **tinyprobe 0.10.0** and the extension **0.2.0**.

- Format: API files with endpoints, params, query, headers, auth sets and bodies. Workflows import them and run `call` / `with` / `expect` / `save` steps, with `sleep`, `until` (polling), generated values (`${$uuid}`, `${$now+2h}`...) and AND/OR checks. Full validation before running, with `file:line:column` and "did you mean".
- CLI: `run`, `call`, `inspect`, `update`, `guide`; console and ndjson reporters; Native AOT binaries for win-x64, linux-x64, linux-arm64 and osx-arm64; install scripts.
- Extension: validation on save, ▶ Call, ▶ Run workflow, Test Explorer, run view, environment picker. Distributed as a `.vsix` from GitHub Releases only.

## How we work

- **Status:** `[ ]` todo · `[~]` in progress · `[x]` done · `[-]` dropped (reason in the task) · `[?]` needs a decision.
- **Areas:** `cli` this repository's code · `guide` `docs/llm-guide.md` and other docs for agents · `ext` tinyprobe-vscode · `repo` project housekeeping.
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
- [ ] **M1-2** `guide` Other sources, a short mapping each: OpenAPI 3 / Swagger 2, Insomnia, Bruno, HAR, `curl` commands.
- [x] **M1-3** `guide` What cannot be translated, and the rule: never invent syntax; skip the request and list it in the report with the reason. Covers pre-request scripts with logic (signatures, computed values), `setNextRequest`, form-data and files, OAuth2 flows, cookies, non-JSON checks, unsupported JSONPath. PR #14.
- [x] **M1-4** `guide` Large collections: inventory first (requests, folders, environments), convert one folder at a time, finish with a coverage report (requests → endpoints, workflows, skipped with reasons). PR #14.
- [x] **M1-5** `repo` Ready-to-paste prompts in the README: migrate a Postman collection, start from an OpenAPI document, scan a backend (exists, review it).
- [x] **M1-6** `guide` Benchmark for the guide: a Postman collection of the test server with variables, scripts and variants (`docs/example/postman/`), a reference conversion, and a procedure to score an agent's conversion: rounds of `inspect` / `run` until it passes, requests covered, invented syntax. Run with at least two agents before and after M1-1…M1-4. *Done when:* the scores are recorded in the spec of M1-1. Run with Claude Code (Sonnet and Haiku); other agents can be added by hand. PR #14.
- [ ] **M1-7** `cli` `tinyprobe run <folder>`: runs every workflow of a folder, one summary, exit code `1` if any failed. Agents and CI run a whole migration with one command. *Spec needed.*
- [ ] **M1-8** `cli` `tinyprobe init`: creates the `tinyprobe/` layout of the guide (`api/`, `workflows/`, `env.example.yaml`), the `.gitignore` entries, and a short `AGENTS.md` section that points agents to `tinyprobe guide`. *Spec needed.*
- [ ] **M1-9** `ext` "tinyprobe: Migrate with AI": pick a collection or an OpenAPI file, build the migration prompt, and hand it to the editor's chat, or copy it to the clipboard when there is no chat.

## M2 — Agents everywhere

Make tinyprobe a tool that agents use directly, not only a format they write.

- [ ] **M2-1** `cli` `tinyprobe mcp`: an MCP server over stdio with the tools `guide`, `inspect`, `call` and `run`, returning the protocol JSON. Native AOT-safe. *Spec needed.*
- [ ] **M2-2** `guide` Ready-made agent packages: a Claude Code skill, Cursor rules, Copilot instructions and `llms.txt`, each a thin wrapper around `tinyprobe guide`.
- [ ] **M2-3** `ext` Completion and hover in tinyprobe files: keys, endpoint names after `call:`, params in `with`, from `inspect --json`. *Spec needed if the protocol needs new data.*
- [ ] **M2-4** `ext` "tinyprobe: Generate from code": uses the VS Code Language Model API, so it runs on the model the user already has (Copilot or another provider), with the guide as instructions and `inspect` / `run` to check the result.

## M3 — CI

- [ ] **M3-1** `cli` JUnit XML report, read by every CI server. *Spec needed.*
- [ ] **M3-2** `cli` An official GitHub Action: installs a pinned `tinyprobe` and runs a folder of workflows, with a job summary.
- [ ] **M3-3** `cli` A small Docker image with the static binary.
- [ ] **M3-4** `cli` Failing steps as GitHub Actions annotations, at the step's line.

## M4 — What Postman users miss

- [ ] **M4-1** `cli` `multipart/form-data` bodies and file uploads. *Spec needed.*
- [ ] **M4-2** `cli` Cookies: a cookie jar per run. *Spec needed.*
- [ ] **M4-3** `cli` Basic auth and OAuth2 client credentials. *Spec needed.*
- [ ] **M4-4** `cli` `for_each`: repeat a step over a list of arguments. *Spec needed.*
- [ ] **M4-5** `cli` Reusable step sequences (a shared "log in"). *Spec needed.*
- [ ] **M4-6** `cli` Namespaced imports (`call: server.login`). *Spec needed.*
- [ ] **M4-7** `ext` Request history: the last calls with their responses, to send again.
- [ ] **M4-8** `ext` Environment editor: see and edit the variables of the selected environment.
- [ ] **M4-9** `ext` Visual request editor for people who prefer forms to YAML; it writes the YAML.

## Parked

Not planned now. We reconsider them after M4.

- Load testing: N concurrent copies of a workflow, with throughput and latency percentiles.
- A cloud service: shared workspaces, run history, dashboards.

## Dropped

- [-] Direct importers (`tinyprobe import postman` and similar), and an AI model or provider inside tinyprobe. The user's agent does both better; see [decision 0001](docs/decisions/0001-ai-does-the-import.md).

## Inbox

New ideas, not planned yet.

- `ext` Publish to the VS Code Marketplace from the release workflow, without a token: Trusted Publishing when Microsoft ships it, or Microsoft Entra ID with a GitHub federated credential. Until then each `.vsix` is uploaded by hand.
