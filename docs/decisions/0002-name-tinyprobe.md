# 0002 — The name is tinyprobe

- **Status:** Accepted
- **Date:** 2026-10-25

## Context

The project was called Foxify. Before inviting people in (ROADMAP M0-5) we checked the name:

- **Trademarks (USPTO):** FOXIFY is registered in class 41 (online casino games, Games Global USA). Nothing was found in software classes 9 and 42. EUIPO and UIBM were not checked.
- **Search and confusion:** "Foxify" is already a well-known Shopify page builder with "Foxify AI" branding (FoxEcom, foxify.io), and a crypto trading platform with the FOX token (foxify.trade). [foxifyjs/foxify](https://github.com/foxifyjs/foxify) is a Node.js web framework (153★) that owns `foxify`, `foxify-cli` and `@foxify/*` on npm, and its audience is ours: backend developers. Several Firefox tools use the name too.
- **Domains:** foxify.com, .io, .dev and .app are all registered by others.

We could use the name, but search results and user attention would go to those other products. The cost of renaming only grows after the launch.

## Decision

The project is renamed **tinyprobe**:

- "tiny": one native binary of a few megabytes, no runtime, instant start. That is the main difference from Postman.
- "probe": it probes the endpoints of an API.

It is written **in lowercase** everywhere, like the command (`tinyprobe run`), and like curl, hurl or k6. That also sets it apart from the unrelated hardware projects called "TinyProbe" (an ETH Zurich ultrasound probe, an ATtiny logic probe). C# namespaces use `Tinyprobe` (`Tinyprobe.Core`), as .NET naming requires.

Checked on 2026-10-25:

- free on npm, NuGet, Homebrew, PyPI and crates.io;
- free on the VS Code Marketplace and Open VSX;
- no USPTO trademark;
- tinyprobe.dev, .sh and .io have no DNS records;
- on GitHub, `tinyprobe` is a user account, and the repositories with that name are hardware projects.

The migration is specified in [spec 0001](../specs/0001-rename-to-tinyprobe.md).

## Consequences

- Existing users of foxify must reinstall: `foxify update` does not cross the rename (a clean break, chosen because there are few users).
- The fox logo no longer matches the name: it needs a new logo (ROADMAP M0-9).
- Older release notes, tags and issues keep the old name.
