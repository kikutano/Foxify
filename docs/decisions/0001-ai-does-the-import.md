# 0001 — The user's AI does the import

- **Status:** Accepted
- **Date:** 2026-10-25

## Context

Foxify wants Postman users to switch without rewriting anything: their collections, environments and tests must become Foxify files quickly. There are two ways to get there:

1. **Direct importers** in the binary (`foxify import postman collection.json`), one per source format.
2. **The user's own coding agent** converts the files, guided by `docs/llm-guide.md` (`foxify guide`), and checks its work with `foxify inspect`, `call` and `run`.

A Postman collection is more than data. It has one request per variant ("Login OK", "Login wrong password"), tests written in JavaScript (`pm.test`, `pm.expect`), and variables set by scripts. A mechanical import copies that structure: the result is valid but not good Foxify, with one endpoint per variant and the tests lost or translated only in trivial cases. An agent can understand the intent. It merges variants into one endpoint called with different `with`, turns assertions into `expect` checks, names steps, and then runs them against the real backend to see that they pass.

## Decision

- Foxify has **no direct importers** and **no AI model or provider** inside. Importing from Postman, Insomnia, Bruno, OpenAPI, HAR, `curl` or backend code is done by the user's agent.
- Foxify invests instead in what makes any agent good at it:
  - the guide, with a mapping for each source and the cases that cannot be translated;
  - precise validation errors (`file:line:column`, suggestions);
  - the `inspect` / `call` / `run` loop;
  - integrations that put these tools in the agent's hands (`foxify mcp`, ready-made agent instructions, the extension's chat commands).
- The quality of conversions is measured with a benchmark (ROADMAP M1-6), not assumed.

## Consequences

- No parser to maintain for each version of each source format. A format change in Foxify means a change in the guide, which `DocumentationTests` keeps valid.
- Conversions depend on the user's agent and are not deterministic. The guide must make the result checkable: a coverage report of what was converted and what was skipped, and workflows that pass with `foxify run`.
- Users without a coding agent have no import path. We accept this: Foxify's audience already uses one, and the extension can use the model the editor already has (ROADMAP M2-4).
- We revisit this decision if the benchmark shows that agents miss requests or invent syntax in ways the guide cannot fix.
