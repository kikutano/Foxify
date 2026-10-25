# Foxify (CLI)

API testing from the command line: endpoints and workflows in YAML, one Native AOT binary (.NET 10).
GitHub: `kikutano/foxify` (this clone's remote still says `kikutano/tyfapi`, the old name: GitHub redirects).
The VS Code extension lives in the sibling repository `../foxify-vscode` and drives this binary through the JSON protocol.

## Commands

```bash
scripts/dev.sh check     # build + unit tests + end-to-end: run before every PR (mirrors CI)
scripts/dev.sh test      # build + unit tests only
scripts/dev.sh e2e       # docs/example workflows + protocol checks against the test server (auto start/stop)
scripts/dev.sh server    # test server on http://localhost:5122, foreground
scripts/dev.sh install   # dev binary (0.0.0-dev) linked as ~/.local/bin/foxify-dev, for the extension
scripts/dev.sh example   # example/ against JSONPlaceholder (internet)
scripts/dev.sh aot       # real Native AOT publish (needs clang + zlib1g-dev, not installed on this machine)
dotnet test Foxify.slnx --filter "FullyQualifiedName~WorkflowLoaderTests"   # one test class
```

`foxify` on the PATH is the installed release (may be older than the sources); use `.dev/foxify/foxify` or `foxify-dev` to try changes.

## Architecture

```
YAML ──► Parsing/WorkflowLoader ──► Definitions (immutable model) ──► Execution/WorkflowEngine ──► Models/WorkflowReport
                                                                                │                      │
                                                         Cli: ConsoleReporter / NdjsonReporter ◄───────┘   Protocol/ProtocolJson (inspect --json, ndjson events)
```

- `src/Foxify.Core/Parsing/WorkflowLoader.cs`: the whole format. Allowed keys per section are arrays at the top of `Loader` (`StepKeys`, `ExpectKeys`, ...); every problem is collected as a `LoadProblem` with `file:line:column` and a "did you mean" (`Suggest`). Validation is complete before any request.
- `src/Foxify.Core/Definitions/Definitions.cs`: the model (`WorkflowDefinition`, `EndpointDefinition`, `CallStep`, `Expectation`, `Condition`, `Operator`). `Template` keeps the YAML node; placeholders are resolved only at run time.
- `src/Foxify.Core/Execution/`: `WorkflowEngine` (steps, retries, `until`), `RequestBuilder`, `TemplateRenderer` + `Scopes` (variable lookup order: step args → saved → env → workflow `variables`), `CheckEvaluator`, `JsonPath` (subset), `Generators` (`${$uuid}`...), `Durations`.
- `src/Foxify.Core/Protocol/ProtocolJson.cs`: hand-written JSON (Utf8JsonWriter) for `inspect --json` and the ndjson events. Contract in `docs/protocol.md`.
- `src/Foxify.Cli/Program.cs`: argument parsing and commands (`run`, `call`, `inspect`, `update`, `guide`); reporters implement `IRunReporter`.
- `tests/Foxify.RealServer.Tests`: ASP.NET test server used by `docs/example` and CI; `tests/Foxify.Core.Tests/Fakes/FakeServer.cs` for unit tests.

## Rules that are easy to break

- **Native AOT.** No reflection, no `System.Text.Json` reflection serialization, YamlDotNet only through the representation model (`YamlNode`). `IsAotCompatible` warns at build time; the real gate is `scripts/dev.sh aot` / CI.
- **Docs are tested.** `DocumentationTests` loads every ```yaml block of the docs; blocks starting with `# file: path` are written to disk so imports resolve. A doc example with invalid syntax fails the tests.
- **`docs/llm-guide.md` is embedded in the binary** (`foxify guide`) and is what coding agents read: update it with every format change.
- **Protocol compatibility.** Adding keys is free; removing or changing the meaning of a key requires bumping `"protocol"` and updating the extension. Keys are `snake_case`, durations `*_ms`.
- **Exit codes**: 0 passed, 1 a step failed, 2 usage/invalid file. Don't change them.
- All docs and code comments in **English**. Comments explain *why*, in short sentences, like the existing ones.
## Roadmap, specs, decisions

- `ROADMAP.md` is the single plan for this repository **and** foxify-vscode: start every session there. Tasks have ids (`M1-7`) and areas (`cli`, `guide`, `ext`, `repo`).
- A task that changes the YAML format, the CLI surface or the protocol needs an *Approved* spec in `docs/specs/` before code: `/spec` writes it, `/add-feature` implements it.
- `docs/decisions/` records choices that constrain the work. Read them before proposing something they rule out: [0001](docs/decisions/0001-ai-does-the-import.md) means no direct importers and no AI model inside Foxify.
- Promotion plans are private and are not kept in this repository.

## Adding a feature (checklist)

Use the `/add-feature` skill. In short, a format feature touches:

1. `Definitions.cs` (model) → `WorkflowLoader.cs` (keys array + parsing + validation with location and suggestion) → `Execution/*` (behavior) → `WorkflowReport.cs` / `ProtocolJson.cs` (if it appears in results) → `ConsoleReporter.cs` (how people see it).
2. Tests: loader (valid + every invalid case with its message), engine with `FakeServer`, protocol JSON if changed. Assert concrete values.
3. Docs: `docs/workflow-format.md` (reference, remove it from "Not supported yet"), `docs/llm-guide.md` (agents), README table, `docs/protocol.md` if JSON changed. Update `docs/example` workflows when the feature can be exercised against the test server (add a test-server route if needed).
4. Extension impact: does `../foxify-vscode` need to show or send something new? If it relies on the feature, raise its `MINIMUM_VERSION`.
5. `scripts/dev.sh check` green.

## Git

- Branch per change: `feature/…`, `fix/…`, `docs/…`, `perf/…`, `ci/…`; merged to `main` through a PR.
- Commit title in the imperative ("Add until (polling) and generated values"), body explains what and why, with measured numbers when relevant.
- Releases: annotated SemVer tag, see README "Releasing" and the `/release` skill.
