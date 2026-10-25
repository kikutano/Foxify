# tinyprobe guide for coding agents

You are going to write API tests for a backend with **tinyprobe**, a command-line API testing tool. This guide is everything you need: the file format, the rules that are easy to get wrong, a procedure to follow and a complete example. Follow it exactly; do not use syntax that is not described here.

> This guide matches the installed version when read with `tinyprobe guide`. Check that tinyprobe is installed with `tinyprobe --version`; if it is not, tell the user to install it: https://github.com/kikutano/tinyprobe#install

## 1. How tinyprobe works

- **API files** declare the backend's endpoints once: method, path, query, headers, auth, body and parameters. No expectations.
- **Workflow files** `import` API files and run **steps** in order. A step `call`s an endpoint `with` arguments, checks the response with `expect` and stores values with `save` for the next steps.
- **Environment files** hold per-environment values (base URL, credentials). Pass one with `-e`.
- `tinyprobe run <workflow.yaml> -e <env.yaml>` validates the whole workflow and its imports, then runs it and stops at the first failing step.
- `tinyprobe inspect <file.yaml>` validates an API or workflow file without sending anything, and lists its endpoints and steps.
- `tinyprobe call <api.yaml> <endpoint> --with name=value -e <env.yaml>` sends one request and prints the whole response (status, headers, body), whatever the status. Use it to see the real shape of a response before writing checks.

Exit codes: `0` all steps passed, `1` a step failed, `2` invalid workflow or arguments (nothing was sent).

## 2. Project layout

Create the files in a `tinyprobe/` folder at the root of the user's repository, unless the user asks otherwise:

```text
tinyprobe/
├── api/
│   └── backend.yaml          # all endpoints of the backend (split by area if it is large)
├── workflows/
│   ├── smoke.yaml            # one workflow per feature or resource
│   ├── auth.yaml
│   └── orders.yaml
├── env.example.yaml          # committed: every variable, with placeholder values
└── env.local.yaml            # NOT committed: real values for the local backend
```

Add `tinyprobe/env.*.yaml` to `.gitignore`, except `env.example.yaml`.

## 3. API file

```yaml
base_url: ${baseUrl}              # prepended to every path of this file

headers:                          # optional: sent by every endpoint of this file
  Accept: application/json

auth:                             # optional: named header sets, picked per endpoint
  bearer:
    Authorization: "Bearer ${token}"
  apikey:
    X-API-Key: ${apiKey}

endpoints:
  get_order:                      # the name a step calls: snake_case, unique
    path: /api/orders/${id}       # path parameters are placeholders
    params:
      id:                         # declare path placeholders as required params
    auth: bearer

  list_orders:
    path: /api/orders
    query: [status, page, pageSize]   # each is sent only when the step passes it
    auth: bearer

  create_order:
    method: POST                  # default GET
    path: /api/orders
    auth: [bearer, apikey]        # one auth set or a list
    params:                       # optional: declared arguments
      quantity: 1                 #   with a default
      productId:                  #   no value: required
    body:                         # a YAML value: sent as JSON
      productId: ${productId}
      quantity: ${quantity}
```

| Endpoint key | Meaning |
|---|---|
| `method` | HTTP verb, default `GET`. |
| `path` | Required. Absolute (`https://...`) or relative to `base_url`. |
| `query` | List of query parameter names. A name is added to the URL only if the step passes it in `with` (or it is a declared param with a default). |
| `headers` | Extra headers. Later headers override earlier ones: file `headers`, then `auth` sets, then endpoint `headers`. |
| `auth` | Name of an auth set of **the same file**, or a list of names. |
| `body` | A YAML mapping or list: sent as JSON. A string (e.g. a `\|` block): sent as is, `text/plain` unless a `Content-Type` header says otherwise. A single placeholder (`body: ${payload}`): the whole body is an argument. |
| `params` | Declared arguments: `name: default` or `name:` (required). **Declare every path placeholder and every required input as a required param**: a step that forgets it is then reported before running, instead of failing at run time. Placeholders of `auth` sets and `base_url` (such as `${token}`, `${baseUrl}`) are not params: do not declare them. |

## 4. Workflow file

```yaml
name: Orders                      # shown in the report
description: Create, read and delete an order.
import:
  - ../api/backend.yaml           # relative to this file

variables:                        # optional defaults; the env file overrides them
  pageSize: 10

settings:                         # optional
  timeout: 10                     # seconds per request (default 30)
  max_retries: 0                  # retries on network errors and timeouts only (default 0)

steps:
  - name: Log in                  # optional; default: the call plus its path/query arguments
    call: login
    with:
      email: ${email}
      password: ${password}
    expect:
      status: 200
    save:
      token: $.accessToken        # stored after every check passed; usable as ${token}

  - sleep: 200ms                  # a pause: ms, s or m
```

A workflow may also contain `base_url`, `headers`, `auth` and `endpoints` itself, for small single-file tests.

### Steps

| Step key | Meaning |
|---|---|
| `call` | Endpoint name. Required, unless the step is a `sleep`. |
| `with` | Arguments: values for the endpoint's placeholders. May use variables: `id: ${orderId}`. An argument the endpoint does not use (not a param, query name or placeholder of its path, headers, auth sets, base URL or body) is an error, so typos such as `pagesize` are caught. Query values are URL-encoded; `true`, `42` are sent as written. |
| `expect.status` | A code (`201`) or a list (`[200, 204]`). **Without it, any 2xx is accepted and anything else fails the step.** |
| `expect.headers` | Checks on response headers, keyed by header name (case-insensitive). Every operator works (`exists`, `in`, `any`, `ne`, `contains`, numeric ones). Header values are text and are compared as text, so quoting the expected value makes no difference: `X-Count: 3` and `X-Count: "3"` are the same check. |
| `expect.body` | Checks on the JSON body, keyed by JSONPath. |
| `expect.duration` | Response time: `{ lt: 500ms }`. Only `gt`, `gte`, `lt`, `lte`. |
| `until` | Repeat the call until conditions pass (polling): see section 6b. |
| `save` | `variable: <JSONPath>`. A path that matches nothing fails the step. |
| `sleep` | `sleep: 500ms`. A sleep step has no `call`, `with`, `expect`, `until` or `save`. Durations use `ms`, `s`, `m`, `h`, `d`. |

## 5. Checks

```yaml
expect:
  status: 200
  headers:
    Content-Type: { contains: json }
  body:
    $.id: ${orderId}                          # plain value: equality
    $.status: { in: [pending, paid] }         # one of these values
    $.total: { gt: 0, lt: 1000 }              # several operators: ALL must pass
    $.tags: { contains: urgent }              # array contains this element
    $.customer.email: { contains: "@" }       # string contains this text
    $.discount: { any: [ null, { gte: 0 } ] } # at least ONE alternative must pass
    $.deletedAt: { exists: false }            # the path is absent
    $.createdAt: { exists: true }             # present, any value (use for timestamps, generated ids)
    $.address: { eq: { city: Rome, zip: "00100" } } # a whole object: every field must match, no extra fields
  duration: { lt: 2s }
```

| Operator | Passes when the actual value |
|---|---|
| *(plain value)* | equals it |
| `eq` | equals it (use to compare a whole object, or to combine equality with other operators) |
| `ne` | does not equal it (also passes when the path is missing) |
| `gt`, `gte`, `lt`, `lte` | is a number greater / less than it |
| `exists` | `true`: the path exists (even if `null`); `false`: it does not |
| `contains` | is an array with an element **exactly equal** to it (objects: same fields, same values), or a string containing it as text |
| `in` | equals one of the values of a list |
| `any` | satisfies at least one alternative of a list; an alternative is a value or a mapping of operators |

Rules:

- **Equality is typed.** `$.id: 1` matches the number `1`, not the string `"1"`. Numbers compare by value (`9.9` equals `9.90`).
- **A mapping is a list of operators only if every key is an operator.** `{ gte: 1, name: x }` is an error; use `eq` to compare an object.
- **Never repeat a JSONPath key** in the same `body` (YAML forbids duplicate keys). Put all conditions in one mapping: `$.total: { gt: 0, lt: 1000 }`.
- **Every check of a step is evaluated and reported**, not only the first failure.
- **Arrays compare in order**: `$.tags: [a, b]` does not match `["b", "a"]`. Use `contains` for membership.
- **A path past the end of an array is missing**: with two items, `$.items[2]: { exists: false }` passes.
- **`contains` on a string compares text**: `$.message: { contains: 404 }` looks for the text `404`.
- **Body checks and `save` need a JSON body.** On a response without one (a `204`, or an error with an empty body) they fail with `The response body is not valid JSON`: check only `status` (and headers) there.

### JSONPath subset

Only these forms are supported:

| Form | Example |
|---|---|
| root | `$` |
| property | `$.user.email`, `$.X-Request-Id` |
| array index | `$.items[0].sku`, `$[2]`, `$.matrix[1][0]` |

**Not supported:** wildcards (`[*]`, `.*`), filters (`[?(@.x)]`), recursive descent (`..`), bracket names (`['a b']`), negative indexes, slices, functions such as `length()`. To check an item of a list, address it by index (`$.items[0].sku`). `contains` on an array works for simple values (`$.tags: { contains: urgent }`) or whole, exactly equal objects.

## 6. Variables and types

Placeholders are `${name}`. Endpoints behave like functions:

- **Declared `params` are local**: they come from the step's `with` or from their default, never from other variables.
- **Any other placeholder** is resolved from, in order: the step's `with`, values stored with `save`, the environment file, the workflow's `variables`.
- An undefined variable fails the step before the request is sent.
- A step can override a saved value for itself only: `with: { token: invalid-token }` sends a wrong token in that step, and the next steps use the saved one again. This works on any endpoint, also on one that declares `params`: `token` is not a param, so `with` can set it.

YAML types matter in bodies and checks:

- Plain scalars are typed: `true`, `false`, `null`, `42`, `9.9`. **Quote values that must stay strings**: `zip: "00100"`, `version: "1.0"`, `phone: "0612345678"`.
- A plain scalar made of **one** placeholder keeps the variable's type: `quantity: ${qty}` with `qty: 2` sends `2`; `active: ${flag}` sends `true` or `false`.
- A placeholder inside a longer text, or inside quotes, produces a string: `name: "user-${n}"`, `id: "${id}"`.
- **Inside `{ }` or `[ ]` (flow style) a placeholder must be quoted**: in `{ id: ${id} }` YAML reads `{` as the start of another mapping and the file is invalid. Write `{ id: "${id}" }` (a string), or use block style (one key per line) when the type matters.

## 6b. Polling and generated values

**Asynchronous operations: use `until`.** When the backend finishes the work later (a job, a queue consumer, a webhook), repeat the call until the result is there. Never use a fixed `sleep` for this.

```yaml
- name: Booking is confirmed
  call: get_booking
  with:
    id: ${bookingId}
  until:                            # same status / headers / body checks as expect
    body:
      $.status: { in: [Confirmed, Rejected] }   # stop waiting on any final state
    every: 1s                       # default 1s
    timeout: 30s                    # default 30s
  expect:                           # evaluated on the last response
    body:
      $.status: Confirmed
```

- While the `until` conditions fail, the step waits `every` and sends the same request again; any status is accepted meanwhile unless `until.status` is set (`until: { status: 200 }` waits for a resource to appear).
- Stop waiting on **every** final state (`Confirmed` or `Rejected`), then check the one you expect in `expect`: the failure then says "Rejected" at once instead of timing out.
- After `timeout` the step fails with `Gave up after N attempts` and the last values.

**Unique values: generated placeholders start with `$`.** `${$uuid}`, `${$timestamp}` (Unix seconds, a number), `${$randomInt}`, `${$randomString}` (8 characters), `${$now}` (UTC, `2026-10-16T09:41:00Z`), `${$now+2h}`, `${$now-30m}`, `${$today}` (`2026-10-16`), `${$today+1d}`.

- **Every use is a new value.** To reuse one (create with an email, then search it), define it in `variables`: a variable is resolved once per run.
- Use them for fields that must be unique on every run (emails, codes, names) and for dates in the future (`pickupAt: ${$now+2h}`).
- For resources that cannot be deleted, prefer a **fixed** name plus "create, accept `409`, find by `search`" when the backend has a search: it keeps the data from growing on every run. Use generated values when there is no way to find an existing resource again.

```yaml
variables:
  noteTitle: "tinyprobe note ${$randomString}"   # one value for the whole run
  remindAt: ${$now+1d}
```

## 7. Environment file

```yaml
# file: tinyprobe/env.example.yaml
baseUrl: http://localhost:8080
email: user@example.com
password: change-me
apiKey: change-me
```

A flat map of variable names to values. Put here everything that changes between environments and **every secret**. Workflows reference them with `${...}`; never write real passwords, tokens or keys in API or workflow files.

## 8. Procedure

Follow these steps in order. If the user gives you a Postman collection or the exports of another API tool, follow section 12 instead.

1. **Find the endpoints.** Prefer an OpenAPI/Swagger document if the project has one. Otherwise read the route definitions, for example:
   - ASP.NET: `app.MapGet/MapPost(...)`, `[HttpGet]`, `[Route]` on controllers
   - Express / Fastify / NestJS: `app.get(...)`, `router.post(...)`, `@Get()`, `@Post()`
   - FastAPI / Flask / Django: `@app.get`, `@router.post`, `@app.route`, `urls.py`
   - Spring: `@GetMapping`, `@PostMapping`, `@RequestMapping`
   - Rails: `config/routes.rb`; Laravel: `routes/api.php`; Go: `http.HandleFunc`, `r.GET`

   For each endpoint note: method, path and path parameters, query parameters, request body fields and validation rules, whether it needs authentication, status codes and response body shape.
2. **Understand authentication**: how a client obtains a token (login endpoint, the field that contains it), which header carries it, API keys. Model each mechanism as an `auth` set.
3. **Find real test data**: seed data, fixtures, migrations, test users, `.env.example`, `docker-compose.yml` (base URL and port). Use values that exist in the backend; never invent ids, emails or expected values. If credentials or the base URL cannot be found, ask the user.
4. **Write the API file** with every endpoint you found. Check it with `tinyprobe inspect tinyprobe/api/<name>.yaml` (exit `0` means valid), then try the main endpoints with `tinyprobe call`, for example `tinyprobe call tinyprobe/api/backend.yaml get_note --with id=1 -e tinyprobe/env.local.yaml`: the checks you write next should match the responses you saw, not the ones you expect. An endpoint that needs a token gets it with `--with token=...` or `--var token=...`, from a `call` of the login endpoint.
5. **Write the workflows**, one per feature or resource:
   - a **smoke** workflow: the main read endpoints answer as expected;
   - **auth**: valid login, wrong password (`401`), invalid token (`401`/`403`, with `with: { token: invalid-token }`), and no token at all. A request without the `Authorization` header needs an endpoint without `auth` (e.g. `me_anonymous`): that is a different request, not a variant, so it is the one case where a second endpoint for the same route is right;
   - asynchronous results (status set by a job, a webhook, a queue): poll them with `until` (section 6b);
   - for each resource: **list and read** (including filters and pagination), **create** with valid data, **validation errors** (`400`/`422`, check the error field), **not found** (`404`), and the **full lifecycle** of a resource the workflow creates itself: create, save its id, read, update, delete, verify it is gone.
6. **Write `env.example.yaml`**, and `env.local.yaml` with the local values if you know them.
7. **Run every workflow**: `tinyprobe run tinyprobe/workflows/<name>.yaml -e tinyprobe/env.local.yaml`. The backend must be running locally; start it if you can, otherwise ask the user to.
   - Exit `2`: the YAML is invalid. Every problem is listed as `file:line:column: message`, with suggestions. Fix them and run again.
   - Exit `1`: a check failed. The report shows each check with its actual value. Decide whether **your test** is wrong (fix it) or **the backend** does not do what its code or documentation says (keep the test and report it to the user). Never weaken a check only to make it pass.
8. **Report to the user**: the files you created, what each workflow covers, the endpoints you did not cover and why, and any failure that looks like a backend bug.

Safety: run workflows only against a **local or test** environment, never against production unless the user explicitly asks. Workflows that create data must delete it, so they can run again.

## 9. Writing good workflows

- **Independent workflows.** Each workflow logs in itself and creates the data it needs. Never rely on another workflow having run first.
- **Leave the data as you found it.** Create a resource, `save` its id, work on it, delete it. Do not modify or delete seed data that other tests may read.
- **Tolerate leftovers.** A failing step stops the workflow, so the delete steps after it do not run and a failed run can leave its data behind (there is no teardown step). Identify your data by the id you saved, not by counts: check `$.total: { gte: 1 }` rather than `$.total: 1` after creating something, and never assert the exact size of a list your workflow adds to.
- **Stable checks.** For values that change on every run (timestamps, generated ids, tokens) check `{ exists: true }`, a type-like condition (`{ gt: 0 }`) or a format (`{ contains: "@" }`), not an exact value.
- **Check what matters**: the status code and the fields the feature is about. A few precise checks are better than copying the whole response.
- **Name the steps** so the report reads like a specification: `Wrong password is rejected`, `Deleted order is gone`.
- **Negative cases pass `with` overrides** instead of duplicating endpoints: the same `login` endpoint with a wrong password; the same `get_order` with `token: invalid-token`.

## 10. Common mistakes

| Mistake | Correct |
|---|---|
| `expected:`, `asserts:`, `extract:`, `functions:` (old syntax) | `expect:`, `expect.body`, `save:`, `endpoints:` |
| `status_code: 200` | `status: 200` |
| `$.total: ">= 3"` | `$.total: { gte: 3 }` |
| `$.total: { ">=": 3 }` | `$.total: { gte: 3 }` |
| `$.a: 1` and `$.a: { lt: 5 }` as two keys | `$.a: { eq: 1, lt: 5 }` |
| `$.items[*].id`, `$.items[?(@.id==1)]` | `$.items[0].id: 1` (by index); `contains` matches whole elements only |
| `zip: 00100` (becomes the number 100) | `zip: "00100"` |
| `with: { id: ${orderId} }` (invalid YAML) | `with: { id: "${orderId}" }`, or block style: `with:` then `id: ${orderId}` on its own line |
| `sleep: 2` | `sleep: 2s` |
| `sleep: 5s` and hoping an async job finished | `until: { body: { $.status: Done }, every: 1s, timeout: 30s }` |
| `${uuid}`, `{{$guid}}`, `Date.now()` | `${$uuid}`, `${$timestamp}`: generated values start with `$` |
| `${$uuid}` written twice for the same value | define it once in `variables`: every use of `${$uuid}` is a new value |
| `duration: 500` or `{ lt: 500 }` | `duration: { lt: 500ms }` |
| an endpoint per variant (`login_ok`, `login_wrong`) | one `login` endpoint, different `with` |
| a token in the API file | `Authorization: "Bearer ${token}"` and `save: { token: ... }` |
| `auth: bearer` where `bearer` is defined in another file | define the auth set in the same API file as the endpoint |
| `path: /api/notes/${id}` without `params` | `params: { id: }`: a forgotten `with.id` is then caught before running |
| `$.total: 1` after creating an item | `$.total: { gte: 1 }`: a previous failed run may have left data |
| body checks on a `204` or an empty `401` | only `status`: there is no JSON body to check |
| `import: [api/backend.yaml]` from `tinyprobe/workflows/` | `import: [../api/backend.yaml]`: paths are relative to the workflow |
| one endpoint per Postman request (`login_ok`, `login_wrong_password`) | one endpoint; each variant is a step with its own `with` (section 12) |
| `{{token}}` left in a converted file | `${token}` |
| the real values of a Postman environment in `env.example.yaml` | placeholders there; real values only in `env.local.yaml` |
| a workflow that relies on a token saved by another workflow | each workflow logs in itself |
| a Postman request or check skipped without a word | a line in `MIGRATION.md` that says what and why |

## 11. Complete example

A backend with JWT login and a notes resource.

```yaml
# file: tinyprobe/api/backend.yaml
base_url: ${baseUrl}

headers:
  Accept: application/json

auth:
  bearer:
    Authorization: "Bearer ${token}"

endpoints:
  login:
    method: POST
    path: /api/auth/login
    params:
      email:
      password:
    body:
      email: ${email}
      password: ${password}

  me:
    path: /api/me
    auth: bearer

  me_anonymous:                   # the same route without auth: a request with no token
    path: /api/me

  list_notes:
    path: /api/notes
    query: [search, page]
    auth: bearer

  get_note:
    path: /api/notes/${id}
    params:
      id:
    auth: bearer

  create_note:
    method: POST
    path: /api/notes
    auth: bearer
    params:
      title:
      pinned: false
    body:
      title: ${title}
      pinned: ${pinned}

  update_note:
    method: PATCH
    path: /api/notes/${id}
    params:
      id:
      changes:
    auth: bearer
    body: ${changes}

  delete_note:
    method: DELETE
    path: /api/notes/${id}
    params:
      id:
    auth: bearer
```

```yaml
# file: tinyprobe/workflows/auth.yaml
name: Authentication
import:
  - ../api/backend.yaml

steps:
  - name: User logs in
    call: login
    with:
      email: ${email}
      password: ${password}
    expect:
      status: 200
      body:
        $.accessToken: { exists: true }
        $.tokenType: { in: [Bearer, bearer] }
    save:
      token: $.accessToken

  - name: Current user is returned
    call: me
    expect:
      status: 200
      body:
        $.email: ${email}

  - name: Wrong password is rejected
    call: login
    with:
      email: ${email}
      password: wrong-password
    expect:
      status: 401

  - name: Invalid token is rejected
    call: me
    with:
      token: invalid-token
    expect:
      status: [401, 403]

  - name: Missing token is rejected
    call: me_anonymous
    expect:
      status: 401
```

```yaml
# file: tinyprobe/workflows/notes.yaml
name: Notes
description: Lifecycle of a note created by the workflow itself.
import:
  - ../api/backend.yaml

variables:
  noteTitle: "tinyprobe note ${$randomString}"   # unique per run, the same in every step

steps:
  - name: Log in
    call: login
    with:
      email: ${email}
      password: ${password}
    save:
      token: $.accessToken

  - name: Create a note
    call: create_note
    with:
      title: ${noteTitle}
    expect:
      status: 201
      body:
        $.id: { exists: true }
        $.title: ${noteTitle}
        $.pinned: false
      duration: { lt: 1s }
    save:
      noteId: $.id

  - name: Title is required
    call: create_note
    with:
      title: ""
    expect:
      status: [400, 422]

  - name: Read the note
    call: get_note
    with:
      id: ${noteId}
    expect:
      body:
        $.id: ${noteId}
        $.title: ${noteTitle}

  - name: Search finds the note
    call: list_notes
    with:
      search: "${noteTitle}"
    expect:
      body:
        $.total: { gte: 1 }
        $.items[0].title: ${noteTitle}

  - name: Pin the note
    call: update_note
    with:
      id: ${noteId}
      changes:
        pinned: true
    expect:
      body:
        $.pinned: true

  - name: Delete the note
    call: delete_note
    with:
      id: ${noteId}
    expect:
      status: [200, 204]

  - name: Deleted note is gone
    call: get_note
    with:
      id: ${noteId}
    expect:
      status: 404
```

```yaml
# file: tinyprobe/env.local.yaml
baseUrl: http://localhost:8080
email: user@example.com
password: change-me
```

Run:

```bash
tinyprobe run tinyprobe/workflows/auth.yaml -e tinyprobe/env.local.yaml
tinyprobe run tinyprobe/workflows/notes.yaml -e tinyprobe/env.local.yaml
```

A failing check looks like this; fix the test or report the backend behavior:

```text
  ✗ Read the note  GET  200  12.4ms  /api/notes/42
      ✓ $.id == 42
      ✗ $.title == "tinyprobe note k3x9q2ab"  actual "Untitled"
```

## 12. Migrating from Postman and other tools

Follow this section when the user gives you exports of another API tool (a Postman collection, its environments) instead of, or as well as, a backend to read. The mapping below is for Postman; for another tool, follow the same procedure and use the closest rows.

### 12.1 Procedure

1. **Inventory first.** Read the export files before writing anything. A Postman collection export is v2.1 when `info.schema` ends in `v2.1.0/collection.json`; for v2.0 or v1, ask the user to export it again as v2.1. List:
   - every folder and request, with its method and URL;
   - the `auth` of the collection, of each folder and of each request;
   - the collection `variable`s, and the keys of each environment file;
   - the scripts (`event` with `listen: prerequest` or `test`) at every level.

   Write this list to `tinyprobe/MIGRATION.md`, one row per request (section 12.4). It becomes the coverage report.
2. **Environments.** Each environment export (`values[]`) becomes `tinyprobe/env.<name>.yaml`, a flat map of its enabled keys. Values of type `secret`, and keys that look like credentials (`password`, `token`, `apiKey`, `secret`, `clientSecret`), go only into `env.local.yaml`, which is in `.gitignore`; `env.example.yaml` gets every key with a placeholder value. Keys that scripts fill at run time (a token, a created id) don't belong in env files: they become `save`.
3. **API file.** Group endpoints by resource, not by folder. Requests that differ only in their values (a wrong password, a missing field, another token) are **one endpoint**: declare its inputs as `params`, and each Postman request becomes a step with its own `with`.
4. **Workflows.** One workflow per folder that tests a flow, with the steps in the folder's order. Each workflow logs in itself (section 9), even when Postman relied on an earlier folder to set the token.
5. **One folder at a time.** For each folder: write its endpoints and its workflow, `tinyprobe inspect` them, `tinyprobe run` the workflow until it passes, and update `MIGRATION.md`. Then move to the next folder. Never convert everything first and validate at the end.
6. **Coverage report.** Finish `MIGRATION.md` (section 12.4) and tell the user the totals: requests converted, requests skipped, checks dropped, failures that look like backend bugs.

### 12.2 Postman to tinyprobe

| Postman | tinyprobe |
|---|---|
| `{{name}}` in the URL, headers or body | `${name}` |
| `{{baseUrl}}/users/:id` and `url.variable` | `base_url: ${baseUrl}`, `path: /users/${id}`, `params: { id: }` |
| `url.query` | `query: [name, ...]`; the values go in the step's `with` |
| `disabled: true` on a header, query parameter or form field | leave it out |
| raw JSON body | a YAML mapping; `"qty": {{qty}}` without quotes becomes `qty: ${qty}`, which keeps the type |
| `urlencoded` body | a string body `"a=${a}&b=${b}"` and the header `Content-Type: application/x-www-form-urlencoded` |
| collection, folder or request `auth`: `bearer`, `apikey` | `auth` sets in the API file; a request with `inherit` (or no `auth`) uses its parent's set, `noauth` uses none |
| `basic` auth | `Authorization: "Basic ${basicAuth}"`, with the base64 of `user:password` in `env.local.yaml` |
| collection `variable`s | workflow `variables` for non-secret defaults, otherwise env files |
| `pm.environment.set("t", pm.response.json().token)` (also `collectionVariables`, `globals`, `pm.variables`) | `save: { t: $.token }` |
| `pm.response.to.have.status(201)`, `pm.response.code === 201` | `expect.status: 201` |
| `pm.response.to.be.success` | no `status` (any 2xx) |
| `pm.response.to.be.ok` | `status: 200` |
| `pm.expect(json.a).to.eql(1)`, `.to.equal(1)` | `$.a: 1` (`eq` for a whole object) |
| `.to.be.above(n)`, `.below(n)`, `.at.least(n)`, `.at.most(n)` | `gt`, `lt`, `gte`, `lte` |
| `.to.include(x)` on an array or a string | `contains` |
| `.to.be.oneOf([a, b])` | `in: [a, b]` |
| `.to.have.property("id")`, `.to.exist` | `{ exists: true }` |
| `.to.be.true`, `.to.be.null` | `true`, `null` |
| `.to.have.lengthOf(2)` on an array | `$.items[1]: { exists: true }` and `$.items[2]: { exists: false }` |
| `pm.response.to.have.header("X-Id")` | `headers: { X-Id: { exists: true } }` |
| `pm.response.headers.get("X-Id")` compared to a value | `headers: { X-Id: value }` |
| `pm.response.responseTime` below `500` | `duration: { lt: 500ms }` |
| `{{$guid}}`, `{{$timestamp}}`, `{{$randomInt}}`, `{{$isoTimestamp}}` | `${$uuid}`, `${$timestamp}`, `${$randomInt}`, `${$now}` |
| other `{{$random...}}` (`$randomFirstName`, `$randomEmail`) | text built on a generated value: `"name-${$randomString}"`, `"user-${$randomString}@example.com"` |
| a value computed in a pre-request script (`Date.now()`, a unique email) | a workflow variable built on a generated value, so it is the same in every step |
| a retry loop (`setTimeout` and `setNextRequest` on the same request) | `until` |
| saved example responses (`response[]`) | not tests: read them to learn the shape of a response, then check it against the real backend with `tinyprobe call` |
| `pm.test("Name", ...)` | the step's `name` |

An example. This collection excerpt:

```json
{
  "info": { "name": "Shop", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
  "auth": { "type": "bearer", "bearer": [{ "key": "token", "value": "{{token}}" }] },
  "item": [
    {
      "name": "Login",
      "request": {
        "method": "POST", "url": "{{baseUrl}}/api/login", "auth": { "type": "noauth" },
        "body": { "mode": "raw", "raw": "{ \"email\": \"{{email}}\", \"password\": \"{{password}}\" }" }
      },
      "event": [{ "listen": "test", "script": { "exec": [
        "pm.test(\"Status is 200\", () => pm.response.to.have.status(200));",
        "pm.environment.set(\"token\", pm.response.json().accessToken);"
      ] } }]
    },
    {
      "name": "Get order",
      "request": { "method": "GET", "url": "{{baseUrl}}/api/orders/7" },
      "event": [{ "listen": "test", "script": { "exec": [
        "const json = pm.response.json();",
        "pm.test(\"Paid or shipped\", () => pm.expect(json.status).to.be.oneOf([\"paid\", \"shipped\"]));",
        "pm.test(\"Two items\", () => pm.expect(json.items).to.have.lengthOf(2));"
      ] } }]
    },
    {
      "name": "Create order",
      "request": {
        "method": "POST", "url": "{{baseUrl}}/api/orders",
        "body": { "mode": "raw", "raw": "{ \"productId\": {{productId}}, \"quantity\": 1 }" }
      },
      "event": [{ "listen": "test", "script": { "exec": [
        "pm.test(\"Created\", () => pm.response.to.have.status(201));"
      ] } }]
    },
    {
      "name": "Create order - quantity zero",
      "request": {
        "method": "POST", "url": "{{baseUrl}}/api/orders",
        "body": { "mode": "raw", "raw": "{ \"productId\": {{productId}}, \"quantity\": 0 }" }
      },
      "event": [{ "listen": "test", "script": { "exec": [
        "pm.test(\"Rejected\", () => pm.response.to.have.status(422));"
      ] } }]
    }
  ]
}
```

becomes one API file, where the two "Create order" requests are one endpoint:

```yaml
# file: tinyprobe/api/shop.yaml
base_url: ${baseUrl}

auth:
  bearer:
    Authorization: "Bearer ${token}"

endpoints:
  login:
    method: POST
    path: /api/login
    params:
      email:
      password:
    body:
      email: ${email}
      password: ${password}

  get_order:
    path: /api/orders/${id}
    params:
      id:
    auth: bearer

  create_order:
    method: POST
    path: /api/orders
    auth: bearer
    params:
      productId:
      quantity: 1
    body:
      productId: ${productId}
      quantity: ${quantity}
```

and one workflow, with the Postman test names as step names:

```yaml
# file: tinyprobe/workflows/orders.yaml
name: Orders
import:
  - ../api/shop.yaml

steps:
  - name: Login
    call: login
    with:
      email: ${email}
      password: ${password}
    expect:
      status: 200
    save:
      token: $.accessToken

  - name: Get order
    call: get_order
    with:
      id: 7
    expect:
      body:
        $.status: { in: [paid, shipped] }
        $.items[1]: { exists: true }
        $.items[2]: { exists: false }

  - name: Create order
    call: create_order
    with:
      productId: ${productId}
    expect:
      status: 201

  - name: Create order - quantity zero
    call: create_order
    with:
      productId: ${productId}
      quantity: 0
    expect:
      status: 422
```

`email` and `productId` go into `env.example.yaml` and `env.local.yaml`; `password` only into `env.local.yaml`. The collection never deletes the order it creates, so `MIGRATION.md` says that this workflow leaves an order behind on every run (section 9).

### 12.3 What cannot be translated

**Never invent syntax.** When something has no tinyprobe equivalent:

1. if the request still makes sense without it, translate the rest and drop only what has no equivalent;
2. otherwise, skip the request.

Either way, write a line in `MIGRATION.md`: the request, what was not translated, and why. A skipped request that is not in the report looks, to the user, like a request that was migrated.

| In Postman | What to do |
|---|---|
| a pre-request script with logic: a signature (HMAC), a hash, a value in a format tinyprobe cannot generate | skip the request, or translate it without that value if the backend accepts it; report it |
| `pm.sendRequest` in a script | a plain request becomes a step before; otherwise report it |
| `postman.setNextRequest` branching (not a retry loop) | translate the main path; report the branches |
| data files, `pm.iterationData` | translate one iteration with the first row; report that `for_each` is not supported yet |
| a `formdata` body, a file upload | skip the request; report it (multipart bodies are not supported yet) |
| the OAuth 2.0 auth helper | client credentials: a token request as the first step, with `save`; other grant types: report them, and ask the user for a token in `env.local.yaml` |
| cookies set by one response and sent by the next | report it (cookies are not supported yet) |
| regex checks (`.to.match`), type checks (`.to.be.a("string")`), JSON schema (`jsonSchema`, `tv4`) | keep what has an equivalent (`{ exists: true }`, `contains` for a fixed part of the text); drop the rest and report it |
| a loop over a response array (`json.forEach`) | check the first items by index; report that the others are not checked |
| XML or HTML responses | check `status` and headers only; report the body checks |
| JSONPath beyond the subset (`[*]`, filters, `..`) | index-based checks, or report it |

### 12.4 The coverage report

`tinyprobe/MIGRATION.md` stays in the repository: it tells the team what the tests cover and what they don't yet. For example:

```markdown
# Migration from Postman

Source: Shop.postman_collection.json (4 requests), environments: Local, Staging.

| Folder | Request | Became | Notes |
|---|---|---|---|
| Auth | Login | `login`, step "Login" in workflows/orders.yaml | |
| Orders | Get order | `get_order`, step "Get order" | |
| Orders | Create order | `create_order`, step "Create order" | leaves an order behind on every run |
| Orders | Create order - quantity zero | `create_order`, step "Create order - quantity zero" | |

Converted: 4 of 4 requests. Skipped: 0. Checks dropped: 0.
```

## 13. Checklist before you finish

- [ ] Every workflow runs with exit code `0` against the local backend, or the remaining failures are reported to the user as possible backend bugs.
- [ ] No real secret in API or workflow files; secrets are in `env.local.yaml`, which is in `.gitignore`.
- [ ] Every endpoint name used by a step exists in an imported API file.
- [ ] Workflows that create data delete it, and can run twice in a row.
- [ ] Values that must stay strings are quoted.
- [ ] Path placeholders and required inputs are declared as `params`.
- [ ] No exact count on a list the workflow adds to (`{ gte: 1 }`, not `1`), and no body checks on responses without a body.
- [ ] No unsupported JSONPath (`*`, `?()`, `..`) and no repeated keys in `expect.body`.
- [ ] Asynchronous results are awaited with `until`, never with a fixed `sleep`.
- [ ] After a migration: `MIGRATION.md` lists every request of the collection, with what it became or why it was skipped, and every dropped check.
- [ ] After a migration: no `{{...}}` placeholder is left, and no Postman `secret` value is in a committed file.
