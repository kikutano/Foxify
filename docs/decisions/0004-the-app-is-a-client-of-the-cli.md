# 0004 — The app is a client of the CLI

- **Status:** Accepted
- **Date:** 2026-10-31

## Context

M5 brings tinyprobe to people who use neither a terminal nor VS Code: they want to click through an API as they do in Postman. A browser page cannot send those requests itself (CORS, cookies, self-signed certificates), so a local program has to, and the engine for it already exists: the `tinyprobe` binary.

Two ways to put them together:

1. **The UI inside the CLI.** A `tinyprobe ui` command serves the page, or downloads it and serves it. One program to install, but the CLI then knows the UI: its pages, its HTTP routes, its versions. Every UI change touches the CLI, and the binary that CI runs grows for a feature CI never uses.
2. **The app as a client.** A separate app (`tinyprobe-app`, its own repository) serves the page and runs `tinyprobe` the way the VS Code extension does. The CLI does not know it exists.

## Decision

- **The app uses the CLI; the CLI does not know the app.** The CLI has no command, option, route or file that exists for the app. `tinyprobe-app` lives in its own repository, with its own releases.
- **The contracts are the public ones:** the YAML format, `inspect --json`, the ndjson events of `run` and `call`, `tinyprobe schema` and the exit codes ([protocol](../protocol.md)). The app reads and sends nothing else.
- **The YAML files are the source of truth.** What the app creates or edits is a tinyprobe file in the user's folder, checked with `tinyprobe inspect` after every save. It runs unchanged with `tinyprobe run` and in CI. The app keeps no collection, request or variable of its own outside those files.
- **The app has no engine.** It parses no YAML to run it and evaluates no check: requests, validation and results come from the binary, as for the extension ([principle 2](../../ROADMAP.md#principles)).
- When the app needs something the CLI does not offer, it is added to the CLI as a general feature of the protocol, with a spec, usable by any tool: never as a command for the app.

## Consequences

- The CLI stays as light as it is: the app adds nothing to the binary used in CI and in Docker.
- The app declares the oldest tinyprobe it works with, as the extension does with `MINIMUM_VERSION`, and uses the `tinyprobe` on the `PATH` or installs its own copy.
- The app is an executable of its own (a small launcher that serves the page), so it needs its own builds for each platform and, on macOS and Windows, its own code signing.
- Editing YAML from forms is done on the text, keeping the user's comments and order, as the extension's environment editor does; the result is checked by `inspect`, not trusted.
- Proposals that put the UI, its assets or its HTTP API in the CLI are ruled out by this decision.
