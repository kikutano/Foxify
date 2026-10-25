---
name: release
description: Prepare a Foxify release (CLI and, if it changed, the VS Code extension): pick the version, write the annotated tag notes, verify, and tag. Use when asked to release, publish or tag a version.
---

# Release Foxify

Pushing a tag publishes binaries to every user (`foxify update`, install scripts): confirm the version and the notes with the user before pushing anything.

## CLI (this repository)

1. On an up-to-date `main`, with a clean tree: `scripts/dev.sh check`. If `clang` is available, also `scripts/dev.sh aot` and run one workflow with `publish/foxify`.
2. Version: `git describe --tags --abbrev=0` gives the last one. SemVer: before 1.0, a breaking format or protocol change bumps the minor and needs a "Breaking change" section.
3. Notes from `git log <last-tag>..HEAD --oneline` (merged PRs): a title line `Foxify X.Y.Z`, then highlights for users with a short YAML example per new feature, then breaking changes with what users must change. Write them in the scratchpad as `notes.md`.
4. Check that README examples do not show an old version string in sample output if the output changed.
5. Show the notes and the tag command to the user. After approval:
   `git tag -a vX.Y.Z --cleanup=whitespace -F notes.md && git push origin vX.Y.Z`
   (`-beta.N` suffix → pre-release). The Release workflow builds win-x64, linux-x64, linux-arm64, osx-arm64 and publishes checksums.

## Extension (`../foxify-vscode`)

Only if it changed since its last tag:

1. `scripts/dev.sh check` there, with the released binary if possible (`FOXIFY_PATH=$(command -v foxify)` after `foxify update`).
2. Bump `version` in `package.json`, add the `CHANGELOG.md` section; if it needs the new CLI, `MINIMUM_VERSION` in `src/protocol.js` and the README "Requirements".
3. Commit, then the annotated tag the same way. Release the CLI first when the extension depends on it: its CI installs the latest CLI release.
