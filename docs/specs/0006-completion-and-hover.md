# 0006 — Completion and hover in tinyprobe files

- **Status:** Draft
- **Roadmap:** M2-3
- **Repositories:** tinyprobe, tinyprobe-vscode
- **PRs:** —

## Problem

People who write tinyprobe files by hand, or fix what an agent wrote, get help only after saving: the problem is underlined, with "did you mean". While typing they get nothing:

- which keys a step, an `expect` or an endpoint takes (`until`? `every`? `max_retries`?), and what they mean;
- the endpoint names they can `call`, the params of the endpoint in `with`, which ones are required;
- what an endpoint does, without opening the API file.

The extension must not learn the format by itself ([rule of the extension](https://github.com/kikutano/tinyprobe-vscode/blob/main/CLAUDE.md): never reimplement tinyprobe logic). The binary has to give it the data. Two pieces are missing:

1. **The keys of the format, with a description each.** Today they live in the arrays at the top of `WorkflowLoader` (`StepKeys`, `ExpectKeys`...) and in the docs.
2. **The endpoints of a workflow that does not load.** While the user types, the workflow is usually invalid, and `inspect --json` then returns only the problems: no endpoints, so nothing to complete after `call:`.

## Proposal

### 1. `tinyprobe schema`

```text
$ tinyprobe schema > tinyprobe.schema.json
```

prints a [JSON Schema](https://json-schema.org) (draft 2020-12) of tinyprobe files, API files and workflows, with a `description` for every key, from the same key arrays the loader validates with. The same schema is committed as `docs/schema/tinyprobe.schema.json`, and a test fails when it differs from what the binary prints.

It serves three readers:

- **the extension**, for the keys to complete at the cursor and their descriptions on hover;
- **other editors**, through the YAML language server (VS Code with the YAML extension, JetBrains, Neovim, Zed) and a modeline at the top of a file:
  ```yaml
  # yaml-language-server: $schema=https://raw.githubusercontent.com/kikutano/tinyprobe/main/docs/schema/tinyprobe.schema.json
  ```
- **agents** that know JSON Schema, linked from `llms.txt`.

What the schema says: the keys allowed in each section, their types, `method` and the check operators as enums, `required` for `call` or `sleep` in a step. What it does not say, and is still checked only by the binary: imports, endpoint names, params, placeholders, JSONPath. The schema helps people type; `inspect` stays the validator.

### 2. Endpoints of an invalid workflow

When a workflow is invalid but its imports load, `inspect --json` adds the endpoints it found to the invalid JSON:

```json
{
  "protocol": 1,
  "valid": false,
  "file": "/home/me/p/tinyprobe/workflows/users.yaml",
  "errors": [ { "file": "tinyprobe/workflows/users.yaml", "line": 14, "column": 11, "message": "unknown endpoint 'get_usr' (did you mean 'get_user'?)", "text": "..." } ],
  "endpoints": [ { "name": "get_user", "method": "GET", "path": "/users/${id}", "params": [...], "arguments": [...], "location": {...} } ]
}
```

- `endpoints` has the same shape as in a valid inspection. It is present only when every import loaded; otherwise it is left out, and a tool knows it has nothing to complete.
- New key only: protocol `1` is unchanged.

### 3. In the extension

- **Keys.** After a newline or a space at the start of a line, the keys allowed at the cursor, from the schema, with their description. The section at the cursor (a step, its `expect`, an endpoint...) is found by indentation, in `src/protocol.js` with unit tests.
- **`call:`** completes the endpoint names of the workflow and of its imports, with the method and path as detail.
- **`with:`** completes the arguments of the called endpoint: required params first, then optional ones with their default, query names and other placeholders (`protocol.endpointInputs`, as ▶ Call does).
- **Hover** on a key: its description. On the endpoint name after `call:`: method, path, params and the file it comes from.
- **Where the data comes from:** the schema once per binary version (`tinyprobe schema`, cached); endpoints from the inspection the extension already runs on open and save, including the endpoints of an invalid workflow. Completion never runs the binary while typing.
- With a binary older than the release that has `tinyprobe schema`, completion and hover are off; everything else works as before.

## Validation

`tinyprobe schema` takes no argument and cannot fail. An argument is a usage error (exit `2`), as for the other commands.

## Protocol

- New command `tinyprobe schema`, documented in `docs/protocol.md`.
- New key `endpoints` in the invalid output of `inspect --json`, when every import loaded.

## Extension

Completion and hover providers for YAML files that are tinyprobe files. `MINIMUM_VERSION` stays `0.10.0`; completion and hover need the version with `tinyprobe schema`, checked like the MCP server (`supportsSchema`).

## Docs

- `docs/protocol.md`: `tinyprobe schema` and the new key.
- `docs/workflow-format.md` and `README.md`: the modeline, for editors other than VS Code.
- `llms.txt`: a link to the schema.
- The extension's README: completion and hover.

## Out of scope

- Completion of variable names in `${...}`, and of JSONPath in `expect.body`. Later, if people ask.
- Validation as you type (before saving). Saving already validates; the binary is the only validator.
- A language server: the providers are small and the extension already runs the binary.

## Acceptance criteria

- [ ] `tinyprobe schema` prints a valid JSON Schema whose keys are exactly those of the loader's key arrays (a test compares them), with a description for each.
- [ ] `docs/schema/tinyprobe.schema.json` equals the output of `tinyprobe schema` (a test).
- [ ] Every example workflow and API file of `docs/` and `example/` validates against the schema (a test, with a small validator for the subset of JSON Schema the schema uses).
- [ ] `inspect --json` of a workflow with an unknown endpoint has `endpoints` from its imports; of a workflow whose import is broken, it has none.
- [ ] Extension: unit tests for the section at the cursor; integration tests for key completion in a step, `call:` and `with:` completion, and hover on a key and on an endpoint.
- [ ] `scripts/dev.sh check` passes in both repositories.

## Open questions

1. **Also use the YAML extension?** Recommended: no for now. The extension's own providers work without any other extension; the schema is there for people who prefer the YAML extension, with the modeline. The alternative is `contributes.yamlValidation`, which adds the schema to the YAML extension when it is installed, but then the keys are completed twice.
2. **Where the schema is published.** Recommended: in the repository (`docs/schema/`, raw URL on `main`) and in every release as an asset. The alternative is [SchemaStore](https://www.schemastore.org), which editors find on their own by file name; it does not fit, because tinyprobe files are found by content, not by a name pattern.
3. **The command name.** Recommended: `tinyprobe schema`. The alternative is `tinyprobe inspect --schema`.
