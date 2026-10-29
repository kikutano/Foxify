# 0016 — Check a response against a JSON Schema

- **Status:** Implemented
- **Roadmap:** M4-16
- **Repositories:** tinyprobe, tinyprobe-vscode (go to definition on the schema's path)
- **PRs:** #34, tinyprobe-vscode #10

## Problem

A body check names the fields it cares about: `$.id: 1`, `$.email: { matches: ... }`. It says nothing about the rest. The contract of a response (every field, its type, which ones are required, no extra ones) is written somewhere else, in a JSON Schema or in the OpenAPI document of the API, and nothing checks the real responses against it.

Postman users check it with `pm.response.to.have.jsonSchema(schema)` or `tv4.validate`, and check types with `.to.be.a("string")`. The migration guide tells agents to drop both and report them (`docs/llm-guide.md`, 12.3). An agent that starts from an OpenAPI document has the schemas of every response, and cannot use them.

## Proposal

A new operator, `schema`, on a body value. `$` is the whole body:

```yaml
steps:
  - call: get_user
    with: { id: 1 }
    expect:
      status: 200
      body:
        $: { schema: ../openapi/openapi.yaml#/components/schemas/User }   # the whole body
        $.address: { schema: ../schemas/address.json }                      # a part of it
        $.id: { schema: { type: integer, minimum: 1 } }                     # a schema written here
```

```text
  ✗ User by id   GET   200   3.1ms  /api/users/1
      $.email schema type "string", actual 42
      $.address.city schema required, actual (missing)
      $.nickname schema additionalProperties false, actual "gae"
```

When the value is valid, the report has one passed check: `$ schema ../openapi/openapi.yaml#/components/schemas/User`.

Rules:

- **The schema is a file or a mapping.** A file path is relative to the file the check is written in (a workflow or a sequence), like `import`. The file is JSON or YAML. After `#`, a JSON Pointer selects a schema inside it, so an OpenAPI document can be used as it is: `openapi.yaml#/components/schemas/User`. A mapping is a schema written in the step, for small checks such as a type.
- **Files are read when the workflow is loaded**, like the CSV of a `for_each`, and the schema is checked then. A missing file, a pointer that leads nowhere or an unknown keyword is an error before any request.
- **One failed check per problem**, at the JSONPath of the value that breaks the rule, with the keyword and the actual value (objects and arrays in short, `{…}` and `[3 items]`: a whole body would bury the line). At most 20 problems are listed for one `schema` check, then one more line: `$ schema: 14 more problems`.
- **`$ref`** works inside the same document (`#/components/schemas/Address`, `#/$defs/address`) and to another file, relative to the file that has the `$ref` (`./address.json`, `common.yaml#/Money`). A `$ref` to a URL (`https://...`) is an error: tinyprobe reads no schema from the network. A schema written in the step has no local `$ref` (`#/$defs/x`): that one belongs in a file.
- **It works where body checks do:** in `expect` and `until`, with other operators in the same mapping, inside `any`. Not on headers or cookies, which are text: `matches` is for those.

### Keywords

One validator for the schemas people find in their projects: JSON Schema drafts 4 to 2020-12, and the schema objects of OpenAPI 3.0 and 3.1. `$schema` is read but does not change the rules.

| Kind | Keywords |
|---|---|
| any value | `type` (a name or a list; `integer` is a number without a fraction), `enum`, `const`, `nullable` (OpenAPI 3.0: also `null`) |
| combining | `allOf`, `anyOf`, `oneOf`, `not`, `$ref` |
| objects | `properties`, `required`, `additionalProperties` (`false` or a schema), `patternProperties`, `minProperties`, `maxProperties` |
| arrays | `items` (a schema, or a list with `additionalItems` as in drafts 4 to 2019-09), `prefixItems`, `minItems`, `maxItems`, `uniqueItems`, `contains` |
| numbers | `minimum`, `maximum`, `exclusiveMinimum`, `exclusiveMaximum` (a number, or `true` as in draft 4 and OpenAPI 3.0), `multipleOf` |
| strings | `minLength`, `maxLength` (in characters), `pattern` (a regular expression that searches, as `matches`), `format` (below) |

- **`format`** is checked for `date-time`, `date`, `time`, `email`, `uuid`, `uri`, `ipv4` and `ipv6`. Other formats are read and not checked.
- **Annotations are read and ignored:** `title`, `description`, `default`, `examples`, `example`, `readOnly`, `writeOnly`, `deprecated`, `$schema`, `$id`, `$comment`, `discriminator`, `xml`, `externalDocs`, `contentMediaType`, `contentEncoding`, the `$defs` and `definitions` that hold schemas for `$ref`, and every key that starts with `x-`.
- **In YAML, `type: [string, null]`** (a real null, the natural way to write it) means the type `"null"`.
- **Any other keyword is an error**, with a suggestion: a typo such as `requird` must not make a schema pass everything. This covers the keywords this spec does not implement (`if`/`then`/`else`, `dependentRequired`, `dependentSchemas`, `dependencies`, `propertyNames`, `unevaluatedProperties`, `unevaluatedItems`, `$dynamicRef`, `$anchor`), which say so in the error.

## Validation

Errors when the workflow is loaded, at the `schema` value, with the place inside the schema after the file (`schema#/...` for a schema written in the step; a file reached through `$ref` is named by its path from the working directory):

```text
workflows/users.yaml:14:23: schema file not found: ../schemas/user.json
workflows/users.yaml:14:23: ../openapi/openapi.yaml has nothing at #/components/schemas/Usr (did you mean 'User'?)
workflows/users.yaml:14:23: ../schemas/user.json#/properties/total: unknown type 'nubmer' (did you mean 'number'?)
workflows/users.yaml:14:23: ../schemas/user.json#/properties/id: unknown keyword 'minimun' in a schema (did you mean 'minimum'?)
workflows/users.yaml:14:23: ../schemas/user.json: 'if' is not supported in schemas yet (docs/workflow-format.md lists the keywords)
workflows/users.yaml:14:23: ../schemas/user.json#/properties/owner/$ref: $ref to a URL is not supported: https://example.com/owner.json
workflows/users.yaml:14:23: schema#/$ref: $ref #/$defs/a points into the step: put the schema in a file to use local references
workflows/users.yaml:14:23: ../schemas/user.json is not valid JSON: ... (line 3)
workflows/users.yaml:14:23: schema needs a file or a mapping, e.g. { schema: ../schemas/user.json }
workflows/users.yaml:9:9: schema works on body values only: headers and cookies are text, use matches
```

## Protocol

No new keys. Each problem is an entry of `assertions` (`subject` the JSONPath of the value, `condition` such as `schema type "string"`, `actual` the value), and `inspect --json` shows the check as `{ "subject": "$", "condition": "schema ../openapi/openapi.yaml#/components/schemas/User" }`.

`tinyprobe schema` gets `schema` in `$defs/condition`.

## Extension

- Completion and hover of `schema` come from `tinyprobe schema`, with no change.
- Go to definition on the path of a `schema` opens the file (`definitionContext` in `src/protocol.js`, which already handles `use`, `import` and `file`). The pointer after `#` is not followed in this spec.
- No new `MINIMUM_VERSION`.

## Docs

- `docs/workflow-format.md`: `schema` in the operator table and a section: files, pointers, `$ref`, the keyword table, the errors.
- `docs/llm-guide.md`:
  - when to use it: a response whose contract is in an OpenAPI document or a schema file; the type of a value (`{ schema: { type: string } }`);
  - starting from OpenAPI (12.5): point at `components/schemas` instead of copying the schema; write the pointer, not a copy;
  - Postman, Insomnia, Bruno: `jsonSchema` and `tv4.validate` become a schema file in `tinyprobe/schemas/` and a `schema` check; `.to.be.a("string")` becomes `{ schema: { type: string } }`;
  - common mistakes: a pointer into `paths` (`#/paths/~1users~1{id}/...`) instead of `components/schemas`; a schema copied into the step when a file exists; `schema` on a header.
- `README.md`: `schema` in the `expect.body` row.
- `docs/protocol.md`: `schema` among the conditions of the schema.
- `docs/example`: `users.yaml` checks `get_user` against `openapi/openapi.yaml#/components/schemas/User` (with its `$ref` to `Address`), a schema file in `docs/example/schemas/` for a list, and an inline type check, so CI runs all three forms.

## Out of scope

- Checking every response of an endpoint against the OpenAPI document automatically (an API file that names its document). API files declare requests and check nothing ([decision 0003](../decisions/0003-endpoints-declare-workflows-run.md)); a workflow says what it checks.
- The keywords listed as not supported, `$ref` to URLs, `$id`-based references, and the formats outside the list. Each can be added when a real schema needs it.
- Following the pointer after `#` in the extension's go to definition.

## Acceptance criteria

- [x] `schema` with a file, a file and a pointer, and a mapping; on `$` and on a part of the body; in `expect` and `until`; with other operators and inside `any`.
- [x] Every keyword of the table, `$ref` within a document and to another file, OpenAPI 3.0 `nullable` and boolean `exclusiveMinimum`/`exclusiveMaximum`, the listed formats.
- [x] One failed check per problem at the value's JSONPath, at most 20 and then a count; one passed check when valid.
- [x] Every error of the Validation section, with its location, before any request.
- [x] Unit tests for the loader (each error), the validator (each keyword, valid and invalid) and the report; `docs/example` runs the three forms against the test server.
- [x] Native AOT: a hand-written validator on `JsonElement`, no reflection; the size of the binary measured before and after, within the budget of `scripts/perf.sh`.
- [x] Docs above; the extension's go to definition on a `schema` path.
- [x] `scripts/dev.sh check` passes, in both repositories.

## Open questions

1. **An operator or a key of `expect`.** Option A, the operator `schema` on a JSONPath, as proposed: `$` is the whole body, `$.data` the inside of an envelope (`{ "data": ..., "meta": ... }`, common), and it reuses how checks already work (with `until`, `any`, other operators). Option B, `expect.schema: file`, for the whole body only: easier to find, but an envelope needs another way, and it is one more key. *Recommendation: the operator (A).*
2. **Schemas written in the step.** Option A, allowed, as proposed: `{ schema: { type: string } }` is the type check that Postman's `.to.be.a()` needs and tinyprobe does not have. Option B, files only: one way to write a schema, but a type check would need a file. *Recommendation: allowed (A); the guide prefers a file for anything longer than a line.*
3. **Keywords tinyprobe does not know.** Option A, an error, as proposed, except annotations and `x-` keys: a misspelled keyword does not make the check pass silently, which is the rule everywhere else in tinyprobe. Option B, ignored, as the JSON Schema standard says: a schema with an unusual keyword works at once, but `requird: [id]` checks nothing and nobody knows. *Recommendation: an error (A).*
4. **`format`.** Option A, the common formats are checked, as proposed (`date-time`, `date`, `time`, `email`, `uuid`, `uri`, `ipv4`, `ipv6`): OpenAPI documents use them for most dates and emails, and people expect them to mean something. Option B, never checked, as the 2020-12 standard does by default: fewer surprises with odd values, but `format: date-time` checks nothing. *Recommendation: check the common ones (A).*
5. **How many problems.** At most 20 per check, then a count, as proposed. A long list of a broken array would bury the first, most useful problems. *Recommendation: 20.*
