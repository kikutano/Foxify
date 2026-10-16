# Workflow format

Foxify separates *what an endpoint is* (method, path, headers, body shape) from *what a test expects* (status, values, timings). Endpoints are declared once and called by name, like functions, from any number of workflows. The complete example lives in [`docs/example/`](example/).

Two kinds of file:

| File | Contains | Reused by |
|---|---|---|
| **API file** | endpoints with their parameters, base URL, auth header sets | any number of workflows |
| **Workflow file** | steps that `call` endpoints `with` arguments, and what they `expect` and `save` | — |

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
foxify run docs/example/workflows/users.yaml -e docs/environment.yaml
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
| `endpoints.<name>.params` | Optional declaration of the arguments: `name: default`, or `name:` for a required one. Declared params are checked before the request is sent. |

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
| `variables` | Default variables. |
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
| `expect.body` | Checks on the JSON body, keyed by JSONPath. |
| `expect.duration` | Check on the response time, e.g. `{ lt: 500ms }`. |
| `save` | `variable: <JSONPath>` pairs stored for the following steps, after every check passed. A path that matches nothing fails the step. |
| `sleep` | A step of its own: waits, e.g. `sleep: 500ms` or `sleep: 2s`. |

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

To combine equality with other conditions write it as `eq`: a plain value is a single condition. A JSONPath cannot be repeated as a key (`$.email: …` twice), since YAML does not allow duplicate keys; foxify reports it before running.

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

## Validation before running

The whole workflow, imports included, is checked before the first request. Every problem is reported at once, as `file:line:column: message`:

- `call` to an endpoint that does not exist, and unknown keys (both with a "did you mean" suggestion);
- a required `param` missing from `with`, an argument in `with` that the endpoint does not use (a typo such as `pagesize`), or an `auth` set that does not exist;
- an invalid JSONPath, status code, operator or duration;
- an imported file that does not exist, or two endpoints with the same name.

```text
  ✗ The workflow is not valid (2 problems)

  workflows/users.yaml:12:5: unknown endpoint 'logn' (did you mean 'login'?)
  workflows/users.yaml:16:5: unknown key 'expcet' in a step (did you mean 'expect'?)
```

## Not supported yet

- Namespaced imports (`call: server.login`) — duplicate names are an error for now.
- Running a whole folder (`foxify run docs/example/workflows/`).
- Repeating a step over a list of arguments (`for_each`).
- Reusable step sequences (e.g. a shared "log in" block).
