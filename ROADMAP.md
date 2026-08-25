# tyfapi — Development Roadmap & Task Tracker

> **Purpose.** This file is the single source of truth for tyfapi development.
> Every coding session starts here: pick the next unblocked task, implement it,
> verify its acceptance criteria, flip its status, and append a Changelog entry.

**Status legend:**

- `[ ]` TODO
- `[~]` IN PROGRESS
- `[x]` DONE
- `[-]` DEFERRED / DROPPED (reason recorded in Notes)
- `[?]` NEEDS DECISION

**Working rules for this file:**

1. Every task has a unique ID (`M<#>-<#>`), description, and acceptance criteria where relevant.
2. A task may only be marked `[x]` when its acceptance criteria are met **and** `dotnet build` + `dotnet test` pass.
3. Split tasks get decimal sub-IDs (e.g. `M1-3.1`).
4. Scope changes and new tasks are added to this file (never tracked only in chat).
5. All documentation created in this repo is written in **English**.
6. When implementing, follow the binding conventions in §10 (from `cline.md` / `agent_ai.md`).

---

## 1. Product Goals (from `README.md` — remain valid for the rewrite)

- Next-generation, **Git-friendly, AI-first** API testing & traffic simulation tool.
- **CLI-First Architecture:** one dependency-free **Native AOT** .NET (C#) binary — near-instant startup (<10ms), zero runtime dependencies, ~10–15MB footprint, CI/CD-friendly.
- **UI layer (later milestones):** React/Svelte + Tailwind with Monaco editor, delivered as a VS Code extension (Webview) and a cloud web app.
- **AI lifecycle:** OpenAPI/Swagger → AI generates flow file → developer tweaks (UI or text) → AOT CLI executes flow/bots → results stream to CI/CD & cloud → AI diagnostics explain failures and suggest fixes.

---

## 2. Baseline Assessment (v0.1 prototype — audited 2026-08-24)

The existing code compiles, but is **rejected as a foundation** for these verified reasons:

| Area | Finding (evidence) |
|---|---|
| Entry point | Two `Main` methods (build warning CS7022); the *active* top-level `Program.cs` hardcodes absolute Windows paths; the documented usage `tyfapi <flow.yaml>` is not wired (`TyfapiConsole.cs` is dead code) |
| Native AOT | csproj sets `PublishAot=true`, but YamlDotNet deserialization is reflection-based (build warning IL3050) → an AOT `dotnet publish` would produce a broken binary |
| Engine | No fail-fast: missing functions / unmet `depends_on` are silently skipped; no HTTP status validation; `settings.timeout` & `max_retries` parsed but unused; `FunctionDefinition.Baseurl` ignored (engine special-cases a `baseUrlDev` variable); Content-Type header stripped and never re-applied on POST bodies |
| Variables | `metadata.variables` and environment files are never seeded into the engine; extraction coerces numbers/booleans to strings; no error on unknown `${var}` |
| Tests | "Integration" tests construct a `FakeHttpMessageHandler` but **never inject it** (engine internally does `new HttpClient()` and hits real `http://localhost`); widespread `Assert.True(true)` smoke assertions |
| Hygiene | Dead code, orphan `test_parsing.cs` / `test_variables.cs` at repo root, empty `docs/tyfapi.json`, `docs/dev_credentials.json` with local URLs, ~15 nullable warnings |

**Decision (approved):** **full ground-up rewrite** of the CLI engine. The YAML flow *format* is kept (backward compatible with the samples in `docs/`); the C# implementation is not.

---

## 3. Architecture Decisions (ADR-lite)

| ID | Status | Decision | Rationale |
|---|---|---|---|
| AD-1 | `[?]` NEEDS DECISION | AOT-safe deserialization: (a) YamlDotNet `StaticDeserializerBuilder` + CodeGen package, (b) JSON-first via `System.Text.Json` source generators with YAML as convenience layer, (c) drop AOT | README mandates Native AOT + zero reflection; must be settled in M0-4 with a real `dotnet publish` smoke test |
| AD-2 | `[x]` | CLI binary is the single source of truth; UIs (M2/M4) shell out to it | CLI-first architecture per README |
| AD-3 | `[x]` | Keep the flat YAML schema (`metadata` / `functions` / `workflow` / `settings`), extend where needed (per-function `baseurl`, `expected_status`, typed extract) | Existing `docs/*.yaml` samples are the AI-generation target; minimal nesting required |
| AD-4 | `[x]` | Engine must be fully testable: HTTP handler, clock and output are injectable — no `new HttpClient()` inside engine code | Verified testability gap in v0.1 |
| AD-5 | `[x]` | Variable precedence: `--set k=v` > env file > `metadata.variables` > extracted values (extraction always overwrites) | Deterministic runs, CI-friendly |
| AD-6 | `[?]` NEEDS DECISION | `depends_on` semantics: strict validation + sequential order (M1) vs. topological sort + parallel execution (M3 bots) | Needed for the M1 error model; parallelism is a M3 concern |

---

## 4. Milestone 0 — Foundation Reset (greenfield)

> Goal: clean, compiling, AOT-publishable skeleton with a real command surface and zero dead code.

- [ ] **M0-1** Remove prototype dead code and orphans: `TyfapiConsole.cs` (or absorb its logic), duplicate `Tyfapi/Program.cs`, root `test_parsing.cs`, `test_variables.cs`, empty `docs/tyfapi.json`, `docs/dev_credentials.json`.
  - *Accept:* no orphan `.cs` at repo root; exactly one entry point in the solution.
- [ ] **M0-2** New solution layout: `src/tyfapi` (AOT-safe core library), `src/tyfapi.cli` (thin executable), `tests/tyfapi.tests`; proper `.gitignore` (`bin/`, `obj/`); remove committed build artifacts.
  - *Accept:* `dotnet build` clean; no `bin/obj` visible in git status.
- [ ] **M0-3** CLI surface v1:
  - `tyfapi validate <flow.yaml>`
  - `tyfapi run <flow.yaml> [--env <env.yaml>] [--set k=v ...] [--timeout <s>] [--retries <n>] [--json-report]`
  - *Exit codes:* `0` success · `1` execution failed · `2` validation failed · `3` usage error.
  - *Accept:* both commands work from an arbitrary CWD; exit codes covered by tests.
- [ ] **M0-4** Settle AD-1: prototype the chosen deserialization path and verify with `dotnet publish -c Release` + running `validate`/`run` from the **published** binary.
  - *Accept:* published binary executes a flow with no reflection fallback; decision recorded in §3.
- [ ] **M0-5** Build hygiene: `TreatWarningsAsErrors=true`, all nullable warnings resolved, reproducible release pipeline script (`build.ps1` / `build.sh`).
  - *Accept:* zero warnings on a clean build.



---

## 5. Milestone 1 — Core Engine (CLI) & Syntax Definition

### 5.1 Syntax & Validation

- [ ] **M1-1** Formal flow schema spec (JSON Schema + prose): `metadata` (name, description, environment, api_version, variables), `functions` (HTTP_REQUEST: method, baseurl, endpoint, headers, body, extract, expected_status), `workflow` (function steps with `depends_on`, `DELAY`), `settings` (timeout, max_retries). Keep flat and AI-friendly.
  - *Accept:* spec doc in `docs/flow-schema.md`; all `docs/*.yaml` samples conform.
- [ ] **M1-2** `tyfapi validate`: full schema + semantic checks (unknown `function_name`, unknown/unsatisfiable `depends_on`, duplicate variable names, JSONPath syntax, valid HTTP methods, delay bounds). Human-readable errors with step/function context.
  - *Accept:* each check covered by a test with an intentional error fixture.
- [ ] **M1-3** AI-generation validation (README requirement): prompt GPT and Claude to convert 3+ real OpenAPI/Swagger specs into our flow format; `tyfapi validate` + `run` them against the mock API with no manual fixes.
  - *Accept:* ≥3 specs converted successfully; results recorded in `docs/ai-validation.md`.
- [ ] **M1-4** Error model: `FlowValidationError`, `ExecutionError` (carries step id, request summary, response status/body); fail-fast: first failing step aborts the flow.
  - *Accept:* no failure mode ends silently; exit code `1` with actionable message.

### 5.2 Execution Engine

- [ ] **M1-5** HTTP execution: GET/POST/PUT/DELETE (+PATCH/HEAD), correct Content-Type (from headers or JSON default), custom headers (incl. `Authorization`), per-function `baseurl`, shared `SocketsHttpHandler` for keep-alive.
  - *Accept:* fake-handler tests assert exact method/URL/headers/body per request.
- [ ] **M1-6** Variable engine: `${var}` substitution in endpoint, headers and body; precedence per AD-5; unknown variable → `ExecutionError` (no silent empty string).
- [ ] **M1-7** Extraction engine: JSONPath `$.a.b`, array indexing `$.items[0].name`; **typed** extraction (numbers, booleans, strings, nested objects/arrays as JSON); clear error when path missing or response not JSON; multi-variable `extract` blocks.
- [ ] **M1-8** `DELAY` steps: `duration_seconds` (decimal), cancellation-aware, no busy-wait.
- [ ] **M1-9** `depends_on`: strict validation + sequential execution per AD-6 (parallelism deferred to M3).
- [ ] **M1-10** `settings.timeout` (per-request + overall run) and `max_retries` (backoff on 429/5xx/timeout) actually wired and tested.
- [ ] **M1-11** Response policy: per-function `expected_status` (default: any 2xx) → fail-fast on mismatch with response body included in the error (feeds M3 AI diagnostics).
- [ ] **M1-12** Console UX: per-step progress (`step N/M · function · status · ms`), final summary (pass/fail, total time, extracted vars count), `--json-report` machine-readable output (foundation for M3/M4).
- [ ] **M1-13** Bots-ready architecture (README "killer feature"): execution core is non-blocking, no static mutable state, executor safely instantiable N times for concurrent identical flows (load layer itself is M3).

### 5.3 Tests

- [ ] **M1-14** Unit tests: parser + validator (schema, every semantic check, JSONPath edge cases). No `Assert.True(true)` — assert concrete values.
- [ ] **M1-15** Engine tests: injected fake `HttpMessageHandler` asserting exact outgoing requests; verify extracted variables reach subsequent steps (headers/body).
- [ ] **M1-16** Integration tests: in-process `HttpListener` mock API (login → token → protected endpoint) driving the real `docs/*.yaml` fixtures end-to-end.
- [ ] **M1-17** AOT gate: automated step running `dotnet publish -c Release -r <rid>` then executing a smoke flow with the **published** binary (regression guard for AD-1).
- [ ] **M1-18** Coverage gate: coverlet, core engine ≥ 80% line coverage, enforced in the CI-ready pipeline script.

---

## 6. Milestone 2 — VS Code Extension (AI-First Developer Experience)

- [ ] **M2-1** Extension skeleton: CLI discovery + auto-download/install of the platform binary; graceful error when binary missing.
- [ ] **M2-2** Webview UI (React/Svelte + Tailwind, Monaco editor) with two-way sync (UI ⇄ flow file) preserving YAML formatting.
- [ ] **M2-3** In-editor AI agent: prompt → flow file generation → auto-rendered step list; per-step editing with live variable/type hints.
- [ ] **M2-4** Run/validate from the UI (spawns CLI as child process), streaming per-step results into the panel.
- [ ] **M2-5** Packaging & publish: VSIX build pipeline, marketplace page, README documentation with GIF of the flow.
  - *Accept:* a user can install, create a flow via AI, run it, and see results without touching a terminal.

---

## 7. Milestone 3 — CI/CD, Load Testing & AI Error Diagnostics

- [ ] **M3-1** `run --bots N --duration <X>`: N concurrent clones of the flow (per AD-6, topological sort + parallel branches), isolated variable state per bot.
  - *Accept:* sustained load for 5min at configurable RPS against the mock API.
- [ ] **M3-2** Load-test metrics: RPS, latency percentiles (p50/p95/p99), error rate; JSON report compatible with M1-12 schema.
- [ ] **M3-3** CI mode: `--fail-fast`, minimal output, stable exit codes (M0-3), JUnit/GitHub Actions annotations.
- [ ] **M3-4** Distribution: official GitHub Action + slim Docker image (AOT static binary, Alpine/Distroless) + `install.sh` for arbitrary shells.
- [ ] **M3-5** AI error diagnostics (README core feature): on failure, send payload/headers/response to a configurable LLM endpoint (opt-in, provider-agnostic); display explanation + suggested fix; never send secrets (redact `Authorization`).
  - *Accept:* 10 seeded failure scenarios (bad JSON, wrong type, 401, 429, timeout…) each produce a correct, actionable explanation.
- [ ] **M3-6** `depends_on` parallel execution if AD-6 chose topological sort (else document sequential-only for M3).

---

## 8. Milestone 4 — Cloud Platform & Advanced Traffic Simulation

- [ ] **M4-1** SaaS dashboard: auth, workspaces, shared specs, saved flows.
- [ ] **M4-2** `--report-to-cloud` CLI sync + webhooks for CI events.
- [ ] **M4-3** Reporting & analytics UI: reliability over time, latency trends, bot/load reports, team dashboards.
- [ ] **M4-4** Distributed cloud load testing: multi-region bot fleets, geo-tagged latency, billing/metering.
- [ ] **M4-5** AI diagnostics integrated into dashboards (failures pre-explained, fix PR-drafted suggestion where possible).

---

## 9. Feature Checklist (README "Milestone 1 Features" — must all be covered)

| Feature | Implemented by | Status |
|---|---|---|
| HTTP execution (GET, POST, PUT, DELETE) | M1-5 | `[ ]` |
| Variable substitution `${var}` | M1-6 | `[ ]` |
| Workflow with functions and delays | M1-8, M1-9 | `[ ]` |
| Environment variables support | M1-6 (AD-5) | `[ ]` |
| Basic JSON response parsing | M1-7 | `[ ]` |
| Variable extraction from responses | M1-7 | `[ ]` |
| Multi-variable extraction | M1-7 | `[ ]` |
| Dependency tracking between functions | M1-9 | `[ ]` |

---

## 10. Binding Conventions (from `cline.md` / `agent_ai.md`)

**YAML flow files (AI-readability):**
- Complete `metadata` (name, description, environment, api_version) and always a `settings` section (timeout, max_retries).
- Always use `depends_on` arrays in the workflow; descriptive, meaningful names; minimal nesting / flat structure; JSONPath in `$.a.b` style.

**C# / .NET (AOT-first):**
- Native AOT compliant: zero reflection at runtime, source generators where needed (AD-1).
- Meaningful, contextual errors (never silent, never generic).
- `HttpClient` over shared `SocketsHttpHandler`; minimize heap allocations; thread-safe; `TreatWarningsAsErrors`.

**Working rules (from `agent_ai.md`):**
- Read files before editing; no assumptions about unverified code; verify `build` + `tests` after every change; non-interactive commands only; stay within the repo scope; update this roadmap as work progresses.

---

## 11. Changelog

| Date | Task ID | Change | Author |
|---|---|---|---|
| 2026-08-24 | — | Roadmap created; v0.1 baseline audited; full rewrite approved (format kept, code replaced); AD-1 & AD-6 flagged for decision | Cline |
| 2026-08-24 | — | `README.md`: added "🎯 Purpose: What Problem Are We Solving?" section (Function / Flow / Environment model, one-click multi-env, AI-generated flows) | Cline |
| 2026-08-24 | — | `README.md`: added minimal YAML flow example in Purpose (login → token extraction → protected resource), aligned with `docs/*.yaml` conventions | Cline |

  - *Accept:* design note in `docs/architecture.md` + stress test running 20 concurrent executor instances against the mock API.
