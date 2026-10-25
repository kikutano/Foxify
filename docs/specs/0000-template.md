# NNNN — Title

- **Status:** Draft | Approved | Implemented | Rejected
- **Roadmap:** M?-?
- **Repositories:** foxify, foxify-vscode
- **PRs:** —

## Problem

Who needs this and why. What they do today, and what goes wrong.

## Proposal

What the user writes and sees. Start with examples, then rules.

```yaml
# the YAML a user (or an agent) writes
```

```text
$ foxify ...
the console output
```

Rules: defaults, precedence, interaction with existing features (`with`, `save`, `until`, variables, imports).

## Validation

Every new error, as it is printed:

```text
file.yaml:12:5: message (did you mean '...'?)
```

## Protocol

New keys in `inspect --json` and in the ndjson events, with examples. New keys only. Removing or changing a key needs a new protocol version: explain why it can't be avoided.

## Extension

What foxify-vscode shows or sends differently. Whether it needs a newer `MINIMUM_VERSION`.

## Docs

- `docs/workflow-format.md`
- `docs/llm-guide.md`: how agents should use it, and a common mistake if there is one
- `README.md`
- `docs/protocol.md`

## Out of scope

What this spec does not do, and where it might go later.

## Acceptance criteria

- [ ] ...
- [ ] `scripts/dev.sh check` passes (and in foxify-vscode if it changed)

## Open questions

- ...
