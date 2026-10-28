# Workflow format

tinyprobe separates *what an endpoint is* (method, path, headers, body shape) from *what a test expects* (status, values, timings). Endpoints are declared once and called by name, like functions, from any number of workflows. The complete example lives in [`docs/example/`](example/).

Two kinds of file:

| File | Contains | Reused by |
|---|---|---|
| **API file** | endpoints with their parameters, base URL, auth header sets | any number of workflows |
| **Workflow file** | steps that `call` endpoints `with` arguments, and what they `expect` and `save` | — |

An API file is declarative: it has no checks and no `save`, and `tinyprobe call` of one endpoint remembers nothing for the next call. Everything that runs and every value passed from one request to the next is in a workflow ([decision 0003](decisions/0003-endpoints-declare-workflows-run.md)).

```text
docs/example/
├── api/
│   └── testserver.yaml        # endpoints of the test server
└── workflows/
    ├── auth.yaml
    ├── users.yaml
    ├── catalog.yaml
    └── http.yaml
```

```bash
tinyprobe run docs/example/workflows/users.yaml -e docs/environment.yaml
tinyprobe run docs/example/workflows -e docs/environment.yaml      # every workflow, one summary
```

## API file

```yaml
base_url: ${baseUrl}

auth:                                  # named header sets
  bearer:
    Authorization: "Bearer ${token}"
  apikey:
    X-API-Key: ${apiKey}

endpoints:
  get_user:
    path: /api/users/${id}
    auth: bearer

  login:
    method: POST
    path: /api/login
    params:
      username: admin                  # default value
      password:                        # no value: required
    body:
      username: ${username}
      password: ${password}
```

| Key | Description |
|---|---|
| `base_url` | Prepended to every `path` of the file. Optional if paths are absolute URLs. |
| `headers` | Headers sent by every endpoint of the file. |
| `auth.<name>` | A named set of headers. |
| `endpoints.<name>` | An endpoint, called by its name. |
| `endpoints.<name>.method` | HTTP verb, default `GET`. |
| `endpoints.<name>.path` | Path, may contain `${…}` placeholders. |
| `endpoints.<name>.query` | List of query parameters. Each one is sent only when the step passes it. |
| `endpoints.<name>.headers` | Extra headers for this endpoint. |
| `endpoints.<name>.auth` | One auth set or a list of them (`[bearer, apikey]`). |
| `endpoints.<name>.body` | A YAML value, sent as JSON (`application/json`); a string, sent as is (`text/plain` unless a `Content-Type` header says otherwise); or a single `${…}` placeholder. |
| `endpoints.<name>.form` | A form body (`application/x-www-form-urlencoded`), instead of `body`. See [Forms and file uploads](#forms-and-file-uploads). |
| `endpoints.<name>.multipart` | A `multipart/form-data` body with text fields and files, instead of `body`. See [Forms and file uploads](#forms-and-file-uploads). |
| `endpoints.<name>.params` | Optional declaration of the arguments: `name: default`, or `name:` for a required one. Declared params are checked before the request is sent. |

### Forms and file uploads

```yaml
# file: api/forms.yaml
base_url: ${baseUrl}
endpoints:
  token:
    method: POST
    path: /oauth/token
    params: { client_id:, client_secret: }
    form:                                  # application/x-www-form-urlencoded
      grant_type: client_credentials
      client_id: ${client_id}
      client_secret: ${client_secret}
      scope: [read, write]                 # a list: scope=read&scope=write

  upload_avatar:
    method: POST
    path: /api/users/${id}/avatar
    params: { id:, avatar: }
    multipart:                             # multipart/form-data
      description: Profile picture         # a text field
      avatar:
        file: ${avatar}                    # a file: its path
        filename: me.png                   # optional, default: the file's name
        content_type: image/png            # optional, default: from the extension
      tags: [profile, public]              # a list: the field once per value
```

```yaml
# file: workflows/avatar.yaml
name: Avatar
import: [../api/forms.yaml]
steps:
  - call: upload_avatar
    with:
      id: 1
      avatar: ../fixtures/avatar.png       # relative to this workflow
    expect:
      status: 201
```

- An endpoint has at most one of `body`, `form` and `multipart`.
- `form` encodes names and values, after resolving placeholders: a password `a&b` is sent as `a%26b`. Numbers and booleans are sent as text, `null` as an empty value.
- In `multipart`, a field is a value (a text field), a mapping with `file` (a file), or a list of them (the field once per item). Fields are sent in the order written.
- **File paths are relative to the file tinyprobe was given**: the workflow for `run` (each workflow of a folder on its own), the API file for `call`, like `import`. Absolute paths are used as they are. The file is read when the step runs, on every attempt; a missing file fails the step.
- `content_type` defaults to the type of the extension (`png`, `jpg`, `jpeg`, `gif`, `webp`, `svg`, `pdf`, `json`, `txt`, `csv`, `xml`, `html`, `zip`), otherwise `application/octet-stream`. Text fields have no `Content-Type`.
- tinyprobe sets the request's `Content-Type` (with the multipart boundary). A `Content-Type` from the file's `headers` or an auth set is not sent; one in the endpoint's own `headers` is an error.
- With `-v` and in `--reporter ndjson`, a multipart body is shown with each file's content replaced by one line, `(file ../fixtures/avatar.png, 79 B)`.
- Under `tinyprobe mcp`, files must be inside the server's working directory.

## Workflow file

```yaml
name: Users
import:
  - ../api/testserver.yaml             # paths are relative to this file

variables:                             # defaults; the env file overrides them
  pageSize: 2

settings:
  timeout: 10                          # seconds per request
  max_retries: 0                       # retries on network errors / timeouts only

steps:
  - name: Create a user
    call: create_user
    with:
      firstName: Test
      email: test.user@example.com
    expect:
      status: 201
      body:
        $.email: test.user@example.com
        $.id: { exists: true }
    save:
      userId: $.id

  - sleep: 200ms
```

| Key | Description |
|---|---|
| `name`, `description` | Shown in the report. |
| `import` | API files whose endpoints the steps can call. |
| `endpoints`, `auth`, `base_url` | Same as in an API file: a workflow can define its own endpoints, handy for small single-file tests. |
| `variables` | Default variables. Each is resolved once per run, the first time it is used, and may use the environment, other variables and generated values (see below). |
| `settings` | `timeout` (seconds per request, default 30) and `max_retries` (retries on network errors and timeouts, default 0; HTTP error status codes are never retried). |
| `steps` | Run in order; the workflow stops at the first failing step. |

### Steps

| Key | Description |
|---|---|
| `call` | Name of the endpoint. |
| `name` | Shown in the report. Without it the step is shown as the call plus its path and query arguments, e.g. `status code=200`; body and header arguments are left out, since they may be secrets. |
| `with` | Arguments for the endpoint's placeholders. They may reference variables (`id: ${userId}`). |
| `expect.status` | Expected status code, or a list of accepted ones (`[200, 204]`). Without it any 2xx status is accepted and anything else fails the step. |
| `expect.headers` | Checks on response headers (case-insensitive names). |
| `expect.cookies` | Checks on cookies (case-sensitive names) in the workflow's cookie jar after the response, see [Cookies](#cookies). |
| `expect.body` | Checks on the JSON body, keyed by JSONPath. |
| `expect.duration` | Check on the response time, e.g. `{ lt: 500ms }`. |
| `until` | Repeat the call until conditions pass (polling), see below. |
| `save` | `variable: <JSONPath>` pairs stored for the following steps, after every check passed. A path that matches nothing fails the step. `variable: { cookie: name }` saves the value of a cookie instead, see [Cookies](#cookies). |
| `sleep` | A step of its own: waits, e.g. `sleep: 500ms` or `sleep: 2s`. Durations use `ms`, `s`, `m`, `h` or `d`. |

## Variables

Endpoints behave like functions:

- **Declared `params` are local.** They come from the step's `with` or from their default, never from global variables. A required param that the step does not pass is an error.
- **Any other placeholder** (for example `${token}` in an auth set) is resolved, from the highest priority, from: the step's `with`, values stored with `save` by earlier steps, the environment file (`-e`), the workflow's `variables`.

So a step can override a saved value for itself only, without changing it for the next steps:

```yaml
- name: Invalid bearer token is rejected
  call: bearer_check
  with:
    token: invalid-token
  expect:
    status: 401
```

An undefined variable fails the step before the request is sent.

**Types in bodies.** A value that is a single placeholder keeps the argument's type: with `active: ${active}` and `with: { active: false }` the JSON is `"active": false`, not `"active": "false"`. Placeholders inside a longer text produce strings. In `with`, plain YAML scalars keep their type (`true`, `42`, `null`); quote them to send a string (`zipCode: "70100"`).

## Polling with `until`

For operations that complete asynchronously (a booking confirmed by a background job, a payment settled by a webhook), `until` repeats the call until its conditions pass:

```yaml
- name: Booking is confirmed
  call: get_booking
  with:
    id: ${bookingId}
  until:                              # same status / headers / body checks as expect
    body:
      $.status: { in: [Confirmed, Rejected] }
    every: 1s                         # pause between attempts (default 1s)
    timeout: 30s                      # give up after this time (default 30s)
  expect:                             # evaluated on the last response only
    body:
      $.status: Confirmed
  save:
    paymentId: $.paymentId
```

- `until` takes `status`, `headers`, `cookies` and `body`, written exactly like in `expect`, plus `every` and `timeout`.
- While its conditions do not hold, the step waits `every` and sends the request again, with the same arguments. Any status is accepted while waiting, unless `until.status` is set (`until: { status: 200 }` waits for a resource to appear).
- When they hold, `expect` and `save` are applied to that last response.
- If they still do not hold after `timeout`, the step fails with `Gave up after N attempts in 30s` and the report shows the values of the last attempt.
- A network error is not a reason to keep waiting: after the `settings.max_retries` retries of that attempt, it fails the step.

## Cookies

Every workflow run has its own cookie jar, empty at the start. A `Set-Cookie` of any response is stored, redirects included, and every later request of the workflow sends the cookies that match its URL (domain, path, `Secure`, expiry). A cookie that expires, or that the server removes (`Max-Age=0`), is no longer sent. A session kept by cookies needs nothing in the API file:

```yaml
name: Cookie session
import:
  - ../api/testserver.yaml

steps:
  - name: Log in
    call: session_login
    with:
      password: password
    expect:
      cookies:
        sid: { exists: true }              # set by this response, or by an earlier one
    save:
      csrf: { cookie: XSRF-TOKEN }         # the value of a cookie, for a header

  - name: A write with the CSRF token
    call: session_note
    with: { csrf: "${csrf}", text: Hello }  # the endpoint sends X-XSRF-TOKEN: ${csrf}
    expect:
      status: 201

  - name: Log out
    call: session_logout
    expect:
      status: 204
      cookies:
        sid: { exists: false }             # removed by the server
```

- **Each workflow starts with no cookies**, also in a suite (`tinyprobe run tinyprobe/workflows`), so workflows do not depend on each other. `tinyprobe call` keeps no cookies from one call to the next ([decision 0003](decisions/0003-endpoints-declare-workflows-run.md)).
- `expect.cookies` and `until.cookies` check cookies by name, with the same operators as headers; values are text. A name refers to a cookie in the jar **after** the response, among those that would be sent to the request's URL. So `exists: true` passes after a login, and `exists: false` passes after a logout or when the cookie was never set. Names are case-sensitive.
- `save: { name: { cookie: XSRF-TOKEN } }` saves the cookie's value (no attributes), read the same way. A cookie that is not in the jar fails the step: `Cannot save 'csrf': no cookie 'XSRF-TOKEN' after this response`.
- A `Cookie` header written in the API file is sent as written, followed by the jar's cookies: `Cookie: lang=it; sid=abc`.
- The recorded request (`-v`, `--reporter ndjson`, the extension's response view) shows the `Cookie` header as it was sent, the jar's cookies included.
- Cookie attributes (`HttpOnly`, `Secure`, `SameSite`) cannot be checked yet; a header check on the text does it meanwhile: `Set-Cookie: { contains: HttpOnly }`.

## Generated values

Placeholders whose name starts with `$` produce a value instead of reading a variable:

| Placeholder | Value |
|---|---|
| `${$uuid}` | a random UUID, e.g. `"3f2a…"` |
| `${$timestamp}` | Unix time in seconds, a number |
| `${$randomInt}` | a random number from 0 to 999999 |
| `${$randomString}` | 8 random lowercase letters and digits |
| `${$now}` | the current UTC time, `"2026-10-16T09:41:00Z"`; with an offset: `${$now+2h}`, `${$now-30m}` |
| `${$today}` | the current UTC date, `"2026-10-16"`; with an offset: `${$today+1d}` |

**Every use generates a new value.** To use the same value in several places (an email that a step creates and a later step searches), define it in `variables`: a variable is resolved once per run.

```yaml
variables:
  newUserEmail: "smoke-${$uuid}@example.com"
  pickupAt: ${$now+2h}

steps:
  - call: create_user
    with: { email: "${newUserEmail}" }
  - call: list_users
    with: { search: "${newUserEmail}" }
```

A lone placeholder keeps the generated type: `count: ${$randomInt}` sends a number, `id: "${$randomInt}"` a string. The environment file is read as plain values: generated values work in `variables`, endpoints and steps.

## Checks

```yaml
expect:
  body:
    $.email: mario@example.com              # equality
    $.pagination.total: { gte: 3 }
    $.price: { gt: 0, lt: 100 }             # several operators: all must pass (see below)
    $.deletedAt: null
    $.id: { exists: true }
    $.tags: { contains: admin }             # array element or substring
    $.code: { ne: ERR_UNKNOWN }
```

| Operator | Meaning |
|---|---|
| *(plain value)* | equal |
| `eq` | equal, to compare a whole object: `$.user: { eq: { name: Ada } }` |
| `ne` | not equal |
| `gt`, `gte`, `lt`, `lte` | numeric comparison; for `duration` the values are times (`500ms`, `2s`, `1m`) |
| `exists` | the path exists (`true`) or not (`false`) |
| `contains` | the array contains the value, or the string contains the text |
| `in` | equal to one of the values of a list: `{ in: [active, pending] }` |
| `any` | at least one of a list of conditions holds: `{ any: [ { lt: 0 }, { gt: 100 } ] }` |

Equality is typed: `$.id: 1` matches the number `1`, `$.id: "1"` matches the string `"1"`; numbers compare by value (`9.9` equals `9.90`). Header values are text, so header checks compare text. A mapping is read as operators only when all its keys are operators; mixing operators and fields is an error. Expected values may contain placeholders (`$.id: ${userId}`). Every check of a step is evaluated and reported, not only the first failure.

### Several conditions on one value

Put several operators in the same mapping: **all of them must pass**. Each one is reported on its own line, so a failure shows exactly which condition did not hold.

```yaml
expect:
  body:
    $.count: { gt: 0, lt: 10 }                               # a range: 0 < count < 10
    $.email: { eq: ada@example.com, contains: "@example.com" } # equality plus another condition
    $.id: { exists: true, gt: 0 }
  headers:
    Content-Type: { contains: json, ne: application/xml }
  duration: { gte: 10ms, lt: 500ms }
```

```text
  ✗ Get user  GET  200  12.4ms  /users/1
      ✓ $.count > 0
      ✗ $.count < 10  actual 12
```

To combine equality with other conditions write it as `eq`: a plain value is a single condition. A JSONPath cannot be repeated as a key (`$.email: …` twice), since YAML does not allow duplicate keys; tinyprobe reports it before running.

### Alternatives (OR)

The operators of a mapping are combined with AND. For OR there are three tools:

```yaml
expect:
  status: [200, 204]                                  # any of these status codes
  body:
    $.status: { in: [active, pending] }               # one of these values
    $.count: { any: [ { lt: 0 }, { gt: 100 } ] }      # one of these conditions
    $.deletedAt: { any: [ null, { exists: false } ] } # null, or not there at all
```

- **`in`** takes a list of values and passes when the actual value equals one of them; equality is typed, as for plain values.
- **`any`** takes a list of alternatives and passes when at least one holds. An alternative is a value (equality) or a mapping of operators, which must all hold: `{ any: [ 0, { gte: 10, lte: 20 } ] }` means "0, or between 10 and 20".
- **`status`** accepts a list of codes.

`in` and `any` work on headers too, and combine with the other operators of the same mapping: `{ in: [admin, owner], ne: guest }`. In the report each one is a single line:

```text
  ✗ get_user id=3  GET  200  352.78ms  /users/3
      ✗ $.id < 2 or (> 5 and < 10)  actual 3
      ✗ $.username in ["Bret","Antonette"]  actual "Samantha"
```

## Editor support

The [tinyprobe extension](https://github.com/kikutano/tinyprobe-vscode) for VS Code completes keys, endpoint names and params, and shows descriptions on hover. In other editors with the YAML language server (VS Code with the YAML extension, JetBrains IDEs, Neovim, Zed), the JSON Schema of the format (`tinyprobe schema`) gives completion and descriptions of the keys; point a file to it with a first line:

```yaml
# yaml-language-server: $schema=https://raw.githubusercontent.com/kikutano/tinyprobe/main/docs/schema/tinyprobe.schema.json
name: Users
steps:
  - sleep: 1s
```

## Validation before running

The whole workflow, imports included, is checked before the first request. Every problem is reported at once, as `file:line:column: message`:

- `call` to an endpoint that does not exist, and unknown keys (both with a "did you mean" suggestion);
- a required `param` missing from `with`, an argument in `with` that the endpoint does not use (a typo such as `pagesize`), or an `auth` set that does not exist;
- an invalid JSONPath, status code, operator or duration, an `until` without a condition, or a `timeout` shorter than `every`;
- an unknown generated value, such as `${$uid}` (with a "did you mean");
- an imported file that does not exist, or two endpoints with the same name.

```text
  ✗ The workflow is not valid (2 problems)

  workflows/users.yaml:12:5: unknown endpoint 'logn' (did you mean 'login'?)
  workflows/users.yaml:16:5: unknown key 'expcet' in a step (did you mean 'expect'?)
```

## Not supported yet

- Namespaced imports (`call: server.login`) — duplicate names are an error for now.
- Repeating a step over a list of arguments (`for_each`).
- Reusable step sequences (e.g. a shared "log in" block).
