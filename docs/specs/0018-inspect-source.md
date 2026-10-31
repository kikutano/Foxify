# 0018 — The file as written, for editors

- **Status:** Approved
- **Roadmap:** M5-4
- **Repositories:** tinyprobe (tinyprobe-app uses it; the extension may later)
- **PRs:** —

## Problem

An editor that changes a tinyprobe file with forms (tinyprobe-app, M5-4) has to fill the forms with what the file says, then write the change back in the right place. `inspect --json` does not tell it either:

- It describes what an endpoint **does**, not what is **written**: `headers` are the effective ones (the file's, the auth set's and the endpoint's, merged), the auth set's name is gone, and a check is a sentence (`"condition": "in [\"a\",\"b\"]"`), not an operator and a value.
- It gives where a block **starts** (`location`), not where it ends, and nothing at all for auth sets, for `use` steps (the steps of a sequence are listed in their place) or for keys such as `import` and `variables`.

The app could parse the YAML itself, but then it would have a second reader of the format, which [decision 0004](../decisions/0004-the-app-is-a-client-of-the-cli.md) rules out: what the app needs from a file comes from the CLI, as a general feature of the protocol.

## Proposal

A new option of `inspect --json`, `--source`: the document gets two more keys, `source` and `blocks`.

```text
$ tinyprobe inspect tinyprobe/api/shop.yaml --json --source
```

For this file:

```yaml
base_url: ${baseUrl}
auth:
  bearer:
    Authorization: "Bearer ${token}"

endpoints:
  login:
    method: POST
    path: /login
    body: { email: "${email}", password: "${password}" }
  get_user:
    path: /users/${id}
    auth: bearer
    headers:
      X-Client: app
```

```json
{
  "protocol": 1,
  "version": "0.21.0",
  "valid": true,
  "kind": "api",
  "endpoints": [ ... as today ... ],
  "source": {
  "base_url": "${baseUrl}",
  "auth": {
    "bearer": { "Authorization": "Bearer ${token}" }
  },
  "endpoints": {
    "login": { "method": "POST", "path": "/login", "body": { "email": "${email}", "password": "${password}" } },
    "get_user": { "path": "/users/${id}", "auth": "bearer", "headers": { "X-Client": "app" } }
  }
},
  "blocks": [
  { "kind": "key", "name": "base_url", "start": { "line": 1, "column": 1 }, "end": { "line": 1, "column": 21 } },
  { "kind": "key", "name": "auth", "start": { "line": 2, "column": 1 }, "end": { "line": 4, "column": 37 } },
  { "kind": "auth", "name": "bearer", "start": { "line": 3, "column": 3 }, "end": { "line": 4, "column": 37 } },
  { "kind": "key", "name": "endpoints", "start": { "line": 6, "column": 1 }, "end": { "line": 15, "column": 20 } },
  { "kind": "endpoint", "name": "login", "start": { "line": 7, "column": 3 }, "end": { "line": 10, "column": 57 } },
  { "kind": "endpoint", "name": "get_user", "start": { "line": 11, "column": 3 }, "end": { "line": 15, "column": 20 } }
]
}
```

For a workflow, `source.steps` is the list of steps as written, `use` steps included, and each step is a block:

```json
{ "kind": "step", "index": 2, "start": { "line": 14, "column": 3 }, "end": { "line": 21, "column": 21 } }
```

Rules:

- **`source` is the inspected file as written, as JSON**: mappings become objects with their keys in order, lists become arrays, and scalars are typed as YAML reads them (`42` a number, `true` a boolean, `~` and an empty value `null`, `"42"` a string). Placeholders are not resolved (`"${token}"`), as everywhere in the protocol. Comments are not in it. Imported files are not in it: inspect them for theirs.
- **`blocks` are where things are written**, in the order of the file:
  - `key`: every top-level key (`name`, `base_url`, `headers`, `auth`, `endpoints`, `import`, `variables`, `steps`, `sequence`, `params`...), with its `name`;
  - `auth`: each auth set, with its `name`;
  - `endpoint`: each endpoint written in the file, with its `name`;
  - `step`: each item of `steps` (or of `sequence` in a sequence file), with its `index` in that list, starting at 0. This is the index in `source.steps`, not the `index` of the steps of `inspect`, which counts the steps of sequences where their `use` is.
- **`start`** is where the block begins: the key of an entry (`login:`), or the `-` of a list item. **`end`** is just after the last character of its value: the comments and blank lines that follow belong to what comes next, so an editor that rewrites a block keeps them. Both are 1-based `{ line, column }`, as `location` is; the file is the inspected one, so they carry no `file`. An editor replaces a block by replacing the text from `start` to `end`.
- **An invalid file** also gets `source` and `blocks` when its YAML can be read: an editor can show the forms and let the user fix the problem. A file whose YAML cannot be read has neither, and its `errors` say why.
- **Without `--source`** nothing changes: the output stays as small as today for agents and for the extension, which do not need the text. The MCP tool `inspect` does not add it.

## Validation

```text
$ tinyprobe inspect shop.yaml --source
Error: Option '--source' works with '--json'.
Run 'tinyprobe --help' for usage.
```

Exit code `2`, as for other wrong arguments.

## Protocol

New keys only, and only with `--source`: `source` and `blocks` at the top level of the `inspect --json` document. `"protocol"` stays `1`. `docs/protocol.md` gets a section, "The file as written".

## Extension

Nothing in this spec. Its own forms (M5-4 in the extension) can use the same keys later.

## Docs

- `docs/protocol.md`: the section above, with the example and the rules.
- `README.md` and the `Usage` text: `--source` among the options of `inspect`.
- `docs/llm-guide.md`: nothing; agents read the files themselves.
- `docs/workflow-format.md`: nothing; the format does not change.

## Out of scope

- Comments in `source`: an editor that keeps comments works on the text, between `start` and `end`.
- Writing files: tinyprobe stays a reader. Editors write the text and check it with `inspect`.
- The positions of keys inside a block (the `path:` of an endpoint): an editor rewrites a whole endpoint, auth set or step.

## Acceptance criteria

- [ ] `source` for an API file, a workflow (with named imports, `variables`, `use`, `sleep`, `for_each`, `until` steps) and a sequence file; scalars typed as YAML reads them; key order kept.
- [ ] `blocks` for every top-level key, auth set, endpoint and step, with `start` and `end` that cut the file exactly: replacing each block's text by itself gives the same file, and the text of each block parses back to its `source`.
- [ ] An invalid file with readable YAML has `source` and `blocks`; a YAML syntax error has neither.
- [ ] `--source` without `--json` is an error with exit code 2; without `--source` the output is unchanged (the existing protocol tests pass as they are).
- [ ] Native AOT: written with `Utf8JsonWriter` like the rest; the size and time budgets of `scripts/perf.sh` hold.
- [ ] Docs above; `scripts/dev.sh check` passes.

## Open questions

1. **An option or always.** Option A, `--source`, as proposed: the default output stays small for agents (MCP `inspect`) and for the extension, which inspects every file on save. Option B, always in `inspect --json`: one form of the output, but every inspection carries the whole file twice. *Recommendation: A.*
2. **The whole file or each block.** Option A, the whole file once in `source`, and positions in `blocks`, as proposed: one obvious place for every value, including keys no block covers (`name`, `variables`). Option B, a `source` inside each endpoint and step of the existing lists: closer to where an editor looks, but `use` steps, auth sets and top-level keys have no entry there. *Recommendation: A.*
3. **Which blocks.** Top-level keys, auth sets, endpoints and steps, as proposed: what the forms of M5-4 rewrite. Deeper ones (a check inside a step) are rewritten with their step. *Recommendation: these four kinds.*
