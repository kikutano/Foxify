# 0008 — Form and multipart bodies, file uploads

- **Status:** Draft
- **Roadmap:** M4-1
- **Repositories:** tinyprobe
- **PRs:** —

## Problem

An endpoint's `body` is JSON or a string. Two common request bodies have no way to be written:

- **File uploads** (`multipart/form-data`): avatars, documents, CSV imports. Postman `formdata`, curl `-F`, Insomnia and Bruno multipart, OpenAPI `multipart/form-data`. Today the guide tells agents to skip these requests and list them in `MIGRATION.md`. In the OpenAPI bench, the 0.11.0 guide made Sonnet build the multipart body by hand as a string, which is fragile and was reported as a failing workflow.
- **Forms** (`application/x-www-form-urlencoded`): OAuth2 token requests, legacy logins, webhooks. Today they are a string body `"a=${a}&b=${b}"` with a `Content-Type` header. The values are not encoded, so a password with `&`, `+` or `=` sends a different request, silently.

## Proposal

Two endpoint keys next to `body`. An endpoint has at most one of the three.

```yaml
endpoints:
  token:
    method: POST
    path: /oauth/token
    params: { client_id:, client_secret: }
    form:                                  # application/x-www-form-urlencoded
      grant_type: client_credentials
      client_id: ${client_id}
      client_secret: ${client_secret}

  upload_avatar:
    method: POST
    path: /api/users/${id}/avatar
    auth: bearer
    params: { id:, avatar: }
    multipart:                             # multipart/form-data
      description: Profile picture         # a text field
      avatar:
        file: ${avatar}                    # a file field: the path of the file to send
      tags: [profile, public]              # a list: the field is sent once per value
```

```yaml
steps:
  - name: Upload the avatar
    call: upload_avatar
    with:
      id: ${userId}
      avatar: ../fixtures/avatar.png       # relative to this workflow file
    expect:
      status: 201
      body:
        $.content_type: image/png
```

### `form`

- A mapping of fields. A value is a scalar, or a list of scalars to repeat the field (`tags=a&tags=b`), in the order written.
- Names and values are encoded (`application/x-www-form-urlencoded`, spaces as `+`). Placeholders are resolved first, then the text is encoded: `${password}` with `a&b` sends `password=a%26b`.
- Numbers and booleans are sent as their text (`true`, `3`). `null` sends an empty value (`field=`).

### `multipart`

- A mapping of fields, sent in the order written. A value is:
  - a **scalar**: a text field;
  - a **file**: a mapping with `file` (required), `filename` and `content_type`;
  - a **list** of scalars or files: the field once per item (several files under one name).
- `file` is a path. **A relative path is relative to the directory of the file tinyprobe was given**: the workflow for `run` (each workflow of a suite on its own), the API file for `call`. This is how `import` works, so a workflow runs the same from any directory: in CI, in the extension, from the MCP server. Absolute paths are used as they are; `~` is not expanded.
- `filename` defaults to the file's name (`avatar.png`). `content_type` defaults to the type of its extension from a short built-in list (`png`, `jpg`/`jpeg`, `gif`, `webp`, `svg`, `pdf`, `json`, `txt`, `csv`, `xml`, `html`, `zip`), otherwise `application/octet-stream`. Text fields have no `Content-Type`.
- Every value may contain placeholders. `file: ${avatar}` takes the path from the step's `with`, an earlier `save`, the environment, or `variables`, as any other placeholder.
- The file is read when the step runs, every attempt (`retries`, `until`), not when the workflow loads, because the path can come from a placeholder.

### Both

- **`Content-Type` is set by tinyprobe** (`multipart/form-data; boundary=…`, `application/x-www-form-urlencoded`). A `Content-Type` from the file's `headers` or from an auth set is not applied to these endpoints. A `Content-Type` in the endpoint's own `headers` is an error, so nobody writes one thinking it is used.
- A step's `with` passes values as for `body`: there is no `with.form` or `with.multipart`. Declare the varying fields as `params`, as in the examples.
- The step's default name stays the call plus its path and query arguments: form and file arguments are not shown, like body arguments.
- **Under `tinyprobe mcp`, a file must be inside the server's working directory** (the workspace). An agent connected only through MCP, with no shell, would otherwise be able to send any file the user can read to any URL.

### Console and recorded requests

The console line is unchanged. With `-v`, in `--reporter ndjson` (`request.body`) and in the extension's response view, a form body is shown as sent (`grant_type=client_credentials&client_id=…`). A multipart body is shown as sent, except that the content of each file is replaced by one line, so a recorded request stays readable and small:

```text
--tinyprobe-6f1c2e
Content-Disposition: form-data; name="description"

Profile picture
--tinyprobe-6f1c2e
Content-Disposition: form-data; name="avatar"; filename="avatar.png"
Content-Type: image/png

(file ../fixtures/avatar.png, 1.2 KB)
--tinyprobe-6f1c2e--
```

## Validation

At load time:

```text
api.yaml:12:5: endpoint 'upload_avatar' has both body and multipart: use one of body, form, multipart
api.yaml:16:9: unknown key 'filname' in a multipart file (did you mean 'filename'?)
api.yaml:15:9: a multipart file needs file: the path of the file to send
api.yaml:20:7: form field 'tags' must be a value or a list of values
api.yaml:18:7: multipart field 'meta' must be a value, a file ({ file: path }) or a list of them
api.yaml:9:7: endpoint 'upload_avatar' sets Content-Type, but a multipart body sets its own: remove the header
```

When the step runs (the step fails as a request error, with no response):

```text
Cannot read the file for multipart field 'avatar': tinyprobe/fixtures/avatar.png was not found
Cannot read the file for multipart field 'avatar': /home/me/.ssh/id_rsa is outside the working directory of the MCP server
```

## Protocol

New keys only, protocol `1`.

- `inspect --json`, each endpoint: `"form"` and `"multipart"`, `null` when absent, next to `"body"`. Fields keep their order. Values are the literal YAML, placeholders included, as `body`:

  ```json
  "multipart": [
    { "name": "description", "value": "Profile picture" },
    { "name": "avatar", "file": "${avatar}", "filename": null, "content_type": null },
    { "name": "tags", "value": "profile" },
    { "name": "tags", "value": "public" }
  ]
  ```

  ```json
  "form": [ { "name": "grant_type", "value": "client_credentials" } ]
  ```

- `request.body` in the ndjson events: the text above. Its meaning does not change (the body as sent); for multipart, file contents are replaced, as stated in `docs/protocol.md`.

## Extension

Nothing required: completion and hover read the keys from `tinyprobe schema`, so `form`, `multipart`, `file`, `filename` and `content_type` are completed and described with no extension change. The response view shows `request.body` as it does today. No new `MINIMUM_VERSION`.

## Docs

- `docs/workflow-format.md`: `form` and `multipart` in the API file reference, with the path rule; remove them from "Not supported yet" if listed.
- `docs/llm-guide.md`:
  - 12.2: Postman `urlencoded` becomes `form`.
  - 12.3: `formdata` and file uploads are translated, no longer skipped. The files go in `tinyprobe/fixtures/`. A Postman `src` that is not in the repository: ask the user for the file, or create a small file of the same type and say so in `MIGRATION.md`.
  - 12.5–12.9: OpenAPI `multipart/form-data` and `application/x-www-form-urlencoded`, Insomnia, Bruno, HAR `postData.params`, curl `-F` (`-F "f=@path"` is a file) and `--data-urlencode`.
  - The common mistake: a hand-built multipart string, or a `Content-Type` header next to `form` or `multipart`.
- `README.md`: the format table.
- `docs/protocol.md`: `form` and `multipart` in `inspect`, multipart in `request.body`.
- JSON Schema (`tinyprobe schema`, `docs/schema/tinyprobe.schema.json`), with descriptions.
- `docs/example`: test-server routes `POST /api/form` and `POST /api/files`, which return the fields and files they received (name, filename, content type, size, SHA-256). A workflow `files.yaml` sends a form with characters that need encoding, and a multipart body with a text field, a list and a small committed image (`docs/example/fixtures/avatar.png`), and checks every value. The OpenAPI bench: `uploadAvatar` becomes translatable; its README says so. The bench is rerun once, and the result is noted.

## Out of scope

- A part with inline content and its own type (a JSON `metadata` part next to a file, as in Spring's `@RequestPart`). It can come later as `{ content: …, content_type: … }` in the same mapping.
- A raw binary body (`--data-binary @file`, a `PUT` of a file without multipart): a later `body: { file: … }`-like key, with its own spec.
- Streaming large files: files are read into memory. Test fixtures are small.
- Response bodies that are files (downloads): checking their size or checksum.

## Acceptance criteria

- [ ] `form` and `multipart` as above, with every validation error at its location.
- [ ] Unit tests: the loader (valid, and every error above), the request builder (encoding, field order, lists, file parts, default and explicit `filename` and `content_type`, the path relative to the workflow and to the API file for `call`, a missing file, the MCP working-directory rule), the recorded multipart text.
- [ ] `docs/example/workflows/files.yaml` passes against the test server, in CI and in `scripts/dev.sh e2e`.
- [ ] `inspect --json` has `form` and `multipart`; the schema has their keys, and its test passes.
- [ ] The guide translates file uploads (Postman, OpenAPI, curl), and the OpenAPI bench converts `uploadAvatar`.
- [ ] `scripts/dev.sh check` passes.

## Open questions

1. **Key names.** `form` for urlencoded and `multipart` for multipart/form-data, two keys with no guessing. The alternative is one `form` key that becomes multipart when a field is a file (as HTTPie does): fewer keys, but the request changes type without anyone writing it. *Recommendation: two keys.*
2. **`form` in this task.** M4-1 is about multipart. `form` is small and uses the same parsing, and it fixes the unencoded values of today's string bodies (which M4-3, OAuth2 client credentials, will use). *Recommendation: include it.*
3. **Relative paths.** Relative to the file tinyprobe was given (workflow for `run`, API file for `call`), like `import` and like Hurl. The alternative is the current directory, like curl: simpler to explain, but a workflow then works only from the directory it was written for. *Recommendation: relative to the file.*
4. **Files under MCP.** Limit files to the server's working directory. Without that, an MCP-only agent can upload any readable file. The limit applies only to `tinyprobe mcp`; `run` and `call` read what the user's own files point to, like curl. *Recommendation: limit.*
