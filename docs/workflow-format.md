# Workflow format v2 (draft)

> Status: **proposal under review**, not implemented yet. The complete example lives in [`docs/example/`](example/).

## Why

In the current format every `function` mixes two things: *what an endpoint is* (method, path, headers, body shape) and *what one test expects* (status, concrete values, asserts). That makes endpoints impossible to reuse: `Login` and `LoginInvalid` are two copies of the same endpoint, `TestStatus200…503` are five, and the final `workflow:` list just repeats every function name once.

v2 splits them:

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
| `endpoints.<name>.body` | A YAML value (sent as JSON), a string (sent as is), or a single `${…}` placeholder. |
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
| `settings` | `timeout`, `max_retries`. |
| `steps` | Run in order; the workflow stops at the first failing step. |

### Steps

| Key | Description |
|---|---|
| `call` | Name of the endpoint. |
| `name` | Shown in the report; defaults to the `call` name. |
| `with` | Arguments for the endpoint's placeholders. They may reference variables (`id: ${userId}`). |
| `expect.status` | Expected status code. |
| `expect.headers` | Checks on response headers (case-insensitive names). |
| `expect.body` | Checks on the JSON body, keyed by JSONPath. |
| `expect.duration` | Check on the response time, e.g. `{ lt: 500ms }`. |
| `save` | `variable: <JSONPath>` pairs stored for the following steps. |
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
    $.price: { gt: 0, lt: 100 }             # several operators: all must pass
    $.deletedAt: null
    $.id: { exists: true }
    $.tags: { contains: admin }             # array element or substring
    $.code: { ne: ERR_UNKNOWN }
```

| Operator | Meaning |
|---|---|
| *(plain value)* | equal |
| `ne` | not equal |
| `gt`, `gte`, `lt`, `lte` | numeric comparison (`duration` also accepts `ms`/`s` values) |
| `exists` | the path exists (`true`) or not (`false`) |
| `contains` | the array contains the value, or the string contains the text |

Equality is typed: `$.id: 1` matches the number `1`, `$.id: "1"` matches the string `"1"`. Every check of a step is evaluated and reported, not only the first failure.

## Validation before running

The whole workflow is checked before the first request, so mistakes are reported immediately with the file and line:

- `call` to an endpoint that does not exist (with a "did you mean" suggestion);
- a required `param` missing from `with`;
- an unknown operator or an invalid duration;
- two imported endpoints with the same name.

## Compatibility with the current format

Files with `functions:` + `workflow:` keep working: each old function is read as an endpoint plus the expectations of the step that calls it, and the run prints a deprecation notice. The old format is removed in 1.0.

| Current format | v2 |
|---|---|
| `functions.<Name>` (request part) | `endpoints.<name>` |
| `functions.<Name>.expected.status_code` | `steps[].expect.status` |
| `extract` + `asserts` | `expect.body` (checks) and `save` (values for later steps) |
| `workflow: - type: function` | `steps: - call:` |
| `type: DELAY` + `duration_seconds` | `- sleep: 1s` |
| `depends_on` | not needed: a missing saved value fails the step |
| `"== 'x'"`, `">= 3"`, `&&` | `x`, `{ gte: 3 }`, several operators on the same path |

## Not in this version

- Namespaced imports (`call: server.login`) — duplicate names are an error for now.
- Running a whole folder (`foxify run docs/example/workflows/`).
- Repeating a step over a list of arguments (`for_each`).
- Reusable step sequences (e.g. a shared "log in" block).
