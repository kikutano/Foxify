---
name: spec
description: Write or update a feature spec in docs/specs/ (from the template) for a ROADMAP task that changes the tinyprobe YAML format, the CLI surface or the JSON protocol, and link it from ROADMAP.md. Use when asked to spec, design or plan a feature, or before /add-feature on such a task.
---

# Write a tinyprobe spec

Arguments: a roadmap task id (`M1-7`) or a short description of the feature.

1. **Read first:** `ROADMAP.md` (the task and its principles), `docs/decisions/`, the specs that already exist in `docs/specs/`, and the parts of `docs/workflow-format.md`, `docs/llm-guide.md` and `docs/protocol.md` the feature touches. Look at the code that would change, so the proposal fits the current model (see CLAUDE.md).
2. **Number:** the next free `NNNN` in `docs/specs/`. File name `NNNN-short-name.md`, copied from `docs/specs/0000-template.md`. Status *Draft*.
3. **Write the proposal from the user's side:** the YAML or command line first, then the output (console and JSON), then the rules. Keep the format flat, with one obvious way to write things, so agents get it right. Reuse existing keys and operators before adding new ones. Every new error has an exact message and a location.
4. **Check the consequences:** protocol (new keys only), the extension, the guide for agents, the test server and `docs/example` (can CI exercise it end to end?), Native AOT (no reflection).
5. **Open questions:** list every choice that is the user's to make, with your recommendation. Don't hide them inside the proposal.
6. **Link it:** in `ROADMAP.md`, add `Spec: [NNNN](docs/specs/NNNN-short-name.md)` to the task and mark it `[~]`.
7. **Stop and ask for review.** The spec becomes *Approved* only when the user says so; only then does implementation start (`/add-feature`). When the PR that implements it is merged, set *Implemented* and the PR link.

A spec is short: it is written for the user, and for whoever implements it later. Leave out what the template does not ask for.
