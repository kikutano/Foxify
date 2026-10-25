# 0001 — Rename Foxify to tinyprobe

- **Status:** Approved
- **Roadmap:** M0-8
- **Repositories:** foxify → tinyprobe, foxify-vscode → tinyprobe-vscode
- **PRs:** —

## Problem

The name Foxify is crowded ([decision 0002](../decisions/0002-name-tinyprobe.md)). Everything users type, install or read must say **tinyprobe**: one name, in lowercase, before the public launch.

## Proposal

### CLI

| Before | After |
|---|---|
| `foxify run`, `call`, `inspect`, `update`, `guide` | `tinyprobe run`, ... (same commands and options) |
| binary `foxify` / `foxify.exe` | `tinyprobe` / `tinyprobe.exe` |
| release assets `foxify-<rid>.tar.gz` / `.zip` | `tinyprobe-<rid>.tar.gz` / `.zip` |
| `FOXIFY_VERSION`, `FOXIFY_INSTALL_DIR` (install scripts) | `TINYPROBE_VERSION`, `TINYPROBE_INSTALL_DIR` |
| console banner `⚡ foxify 0.9.0` | `⚡ tinyprobe 0.10.0` |
| user-agent `foxify/<version>` | `tinyprobe/<version>` |
| projects, namespaces `Foxify.Core`, `Foxify.Cli`, `Foxify.slnx` | `Tinyprobe.Core`, `Tinyprobe.Cli`, `Tinyprobe.slnx` |
| folder suggested to agents `foxify/` | `tinyprobe/` |
| dev binary `foxify-dev`, `FOXIFY_PORT` | `tinyprobe-dev`, `TINYPROBE_PORT` |

Unchanged: the YAML format, the JSON protocol (`"protocol": 1`) and the exit codes. Files written for Foxify run unchanged with tinyprobe.

The test server's sample values (`tyfapi-test-token`, `foxify-test-api-key`...) become `tinyprobe-...`. The docs and examples change with them.

### Existing users: a clean break

- `foxify update` downloads `foxify-<rid>` assets from the latest release, and refuses a binary that is not named `foxify`. tinyprobe releases publish only `tinyprobe-<rid>` assets, so `foxify update` stops at 0.9.0 with a download error. We accepted this: there are few users.
- The README says how to switch: run the install script, then delete the old `foxify` binary.
- The install scripts print a notice when they find an old `foxify` binary in the install folder: they don't delete it themselves.

### VS Code extension

| Before | After |
|---|---|
| extension `kikutano.foxify-vscode`, display name "Foxify" | `kikutano.tinyprobe-vscode`, "tinyprobe" |
| commands `foxify.runWorkflow`, ... "Foxify: Run Workflow" | `tinyprobe.runWorkflow`, ... "tinyprobe: Run Workflow" |
| settings `foxify.path` (default `foxify`), `foxify.validateOnSave`, `foxify.variables` | `tinyprobe.path` (default `tinyprobe`), `tinyprobe.validateOnSave`, `tinyprobe.variables` |
| output channel and messages "Foxify" | "tinyprobe" |
| `foxify-vscode.vsix` | `tinyprobe-vscode.vsix` |
| `FOXIFY_PATH`, `FOXIFY_EXAMPLES`, `FOXIFY_REPO` (dev and tests) | `TINYPROBE_PATH`, `TINYPROBE_EXAMPLES`, `TINYPROBE_REPO` |
| `MINIMUM_VERSION` 0.9.0 | 0.10.0, the first version named tinyprobe |

It is a new extension id, so VS Code does not upgrade the old one: users uninstall "Foxify" and install "tinyprobe". Old `foxify.*` settings are not read; the CHANGELOG says so. The extension was only distributed as a `.vsix` with few users, so no migration code is needed.

### Versions

- CLI **0.10.0**, with a "Breaking change: Foxify is now tinyprobe" section in the release notes.
- Extension **0.2.0**.

## Rollout order

Every URL in the new code points to `kikutano/tinyprobe` and `kikutano/tinyprobe-vscode`, and the install scripts on `main` download `tinyprobe-<rid>` assets. So:

1. Rename the GitHub repositories: `kikutano/Foxify` → `kikutano/tinyprobe`, `kikutano/foxify-vscode` → `kikutano/tinyprobe-vscode`. GitHub redirects the old URLs, so 0.9.0 installs and old links keep working until step 2.
2. Merge the CLI PR and immediately tag `v0.10.0`. Between the merge and the end of the Release workflow (about 10 minutes), the install script on `main` finds no `tinyprobe-<rid>` asset.
3. Merge the extension PR: its CI installs the latest CLI release and clones its sources at that tag, so it needs step 2. Then tag `v0.2.0`.
4. Update the local remotes, the VS Code workspace and the private promotion plan.

## Out of scope

- A new logo and extension icon: ROADMAP M0-9.
- Renaming the local clone folders.
- Domains and social handles (promotion plan, private).

## Acceptance criteria

- [ ] No occurrence of `foxify` / `Foxify` / `FOXIFY` / `tyfapi` is left in either repository, except where it tells the history or helps users switch:
  - the decision records and this spec;
  - the roadmap and CLAUDE.md lines about the old name;
  - the "Coming from Foxify" notes of the READMEs;
  - the install scripts' notice about the old binary;
  - the extension CHANGELOG.
- [ ] `scripts/dev.sh check` passes in both repositories. The extension's integration tests pass with the renamed dev binary.
- [ ] CI is green on both PRs.
- [ ] After the rollout: the install script installs `tinyprobe` 0.10.0, and `tinyprobe update --check` reports it as up to date.
