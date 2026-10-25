# 0002 — The name is tinyprobe

- **Status:** Accepted
- **Date:** 2026-10-25

## Context

Before inviting people in (ROADMAP M0-5) the project needed a name that is free for a developer tool. It must be free on GitHub, the package registries, the VS Code Marketplace and Open VSX. It must not collide with a trademark in the software classes. And searching for it should find this project, not a better-known product. The first working name failed the last test: a popular e-commerce app, a crypto platform and a Node.js web framework already used it.

## Decision

The project is **tinyprobe**:

- "tiny": one native binary of a few megabytes, no runtime, instant start. That is the main difference from Postman.
- "probe": it probes the endpoints of an API.

It is written **in lowercase** everywhere, like the command (`tinyprobe run`), and like curl, hurl or k6. That also sets it apart from the unrelated hardware projects called "TinyProbe" (an ETH Zurich ultrasound probe, an ATtiny logic probe). C# namespaces use `Tinyprobe` (`Tinyprobe.Core`), as .NET naming requires.

Checked on 2026-10-25:

- free on npm, NuGet, Homebrew, PyPI and crates.io;
- free on the VS Code Marketplace and Open VSX;
- no USPTO trademark (EUIPO and UIBM not checked);
- tinyprobe.dev, .sh and .io have no DNS records;
- on GitHub, `tinyprobe` is a user account, and the repositories with that name are hardware projects.

## Consequences

- Binary, release assets, install scripts, namespaces, the extension id, its commands and settings, and the repositories (`kikutano/tinyprobe`, `kikutano/tinyprobe-vscode`) all use the name.
- The current logo is a fox, which doesn't match the name: it needs a new logo (ROADMAP M0-9).
