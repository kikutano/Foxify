# Contributing to tinyprobe

Thanks for helping. This repository is the `tinyprobe` CLI: the YAML format, the engine, the JSON protocol and the guide for coding agents. The VS Code extension lives in [tinyprobe-vscode](https://github.com/kikutano/tinyprobe-vscode). Both follow the same [roadmap](ROADMAP.md).

## Ways to help

- **Report a bug**: a workflow that runs differently from what the docs say, a wrong validation error, a crash. Use the *Bug report* form.
- **Report an agent mistake**: your coding agent (Claude Code, Cursor, Copilot, Codex, ...) wrote tinyprobe files that were wrong, or got stuck while converting a Postman collection, an OpenAPI document or your backend. tinyprobe relies on agents doing that work ([decision 0001](docs/decisions/0001-ai-does-the-import.md)), so these reports improve [`docs/llm-guide.md`](docs/llm-guide.md) directly. Use the *Agent mistake* form.
- **Propose a feature**: start from the problem you have. Use the *Feature request* form, and check the [roadmap](ROADMAP.md) first: it may be planned already.
- **Pick a task**: issues labelled `good first issue`, or a `[ ]` task of the roadmap. Say in the issue that you are working on it.
- **Questions** go to [Discussions](https://github.com/kikutano/tinyprobe/discussions).

## Build and test

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download), plus `curl` and `jq` for the end-to-end checks.

```bash
scripts/dev.sh check     # build, unit tests, end-to-end workflows and protocol checks: what CI runs
scripts/dev.sh test      # build and unit tests only
scripts/dev.sh server    # the test server on http://localhost:5122
scripts/dev.sh install   # a development binary, linked as ~/.local/bin/tinyprobe-dev
```

`scripts/dev.sh e2e` starts the test server itself when it is not running. The Native AOT binary (`scripts/dev.sh aot`) also needs `clang` and `zlib1g-dev` on Linux, or the C++ workload on Windows. CI builds it on every push, so you don't need it locally. On Windows, run the script from Git Bash or WSL.

[CLAUDE.md](CLAUDE.md) maps the code: where the format is parsed, where steps run, how results become JSON. It is written for coding agents but is just as useful to people.

## How changes are made

1. **Small changes** (a fix, docs, tests) need no discussion: open a pull request.
2. **Changes to the YAML format, the CLI surface (commands, options, output) or the JSON protocol** need a spec first. They are public contracts, read by people and by coding agents. Open an issue, or propose a spec in `docs/specs/` from the [template](docs/specs/0000-template.md). Code starts once the spec is approved.
3. Decisions that constrain future work are recorded in [`docs/decisions/`](docs/decisions/). Read them before proposing something they rule out, such as built-in importers.

## Rules for code

- **Native AOT**: no reflection. JSON is written by hand with `Utf8JsonWriter`; YAML is read through YamlDotNet's representation model only.
- **Errors are precise**: every validation problem has a `file:line:column`, a clear message and a "did you mean" for typos. All problems are reported at once, before any request is sent.
- **Tests assert concrete values**: loader tests for valid and invalid input (with the exact message), engine tests against `FakeServer`, protocol tests when JSON changes.
- **Docs are tested**: every ```yaml block of the README, `docs/workflow-format.md` and `docs/llm-guide.md` must load. A format change updates all three. `docs/llm-guide.md` is embedded in the binary (`tinyprobe guide`).
- **The protocol only grows**: new keys are fine; removing or changing one needs a new protocol version.
- English for code, comments and docs. Comments explain *why*.

## Commits and pull requests

- One topic per pull request, on a branch such as `feature/run-folder`, `fix/until-timeout`, `docs/postman-guide`.
- Commit titles in the imperative ("Add until (polling) and generated values"). The body says what changed and why, with measured numbers when they matter.
- `scripts/dev.sh check` passes. Mark the roadmap task as done in the same pull request, if there is one.

## Coding agents

Using Claude Code or another agent to contribute is welcome. [CLAUDE.md](CLAUDE.md) and the skills in `.claude/skills/` (`/spec`, `/add-feature`, `/release`) describe how work is done here. The same rules apply as for people: a spec before format changes, and `scripts/dev.sh check` before the pull request.

## License

By contributing you agree that your contributions are licensed under the [MIT License](License.md).
