# 0013 — Namespaced imports

- **Status:** Implemented
- **Roadmap:** M4-6
- **Repositories:** tinyprobe
- **PRs:** [#30](https://github.com/kikutano/tinyprobe/pull/30)

## Problem

A workflow imports API files, and their endpoints join one list of names. Two endpoints with the same name are an error:

```text
orders.yaml:4:5: endpoint 'login' is already defined at api/users.yaml:12:3
```

The common case is a backend made of several services (microservices, an API gateway, a third-party API next to your own), one API file each. They all have `login`, `health` and `get_user`, so a workflow that crosses two services cannot import both. Today an agent renames the endpoints by hand (`users_login`, `billing_login`), and the API files stop matching the services they describe.

## Proposal

An import can have a name. Its endpoints are then called as `<name>.<endpoint>`.

```yaml
name: Checkout
import:
  - ../api/shop.yaml                     # as today: list_products, create_order...
  - billing: ../api/billing.yaml         # billing.login, billing.create_invoice...
  - users: ../api/users.yaml             # users.login, users.get_user...

steps:
  - call: users.login
    with: { password: "${password}" }
    save: { token: $.token }

  - call: create_order
    with: { productId: p-1 }
    save: { orderId: $.id }

  - call: billing.create_invoice
    with: { orderId: "${orderId}" }
    expect: { status: 201 }
```

```text
  ✓ users.login               POST   200    18.20ms  /auth/login
  ✓ create_order              POST   201    25.11ms  /orders
  ✓ billing.create_invoice    POST   201    31.47ms  /invoices
```

Rules:

- **An import is a path, as today, or a mapping with one `name: path` pair.** A plain path keeps the names as they are, so every workflow written today works unchanged.
- **The name** is made of lowercase letters, digits and `_`, and starts with a letter (`billing`, `users_v2`). Within a workflow, one name stands for one file.
- **What a name prefixes.** Every endpoint of the file becomes `<name>.<endpoint>` for `call`, for the step's default name and in reports. Nothing else changes:
  - an API file keeps its own `base_url`, `headers` and `auth` sets;
  - each file still has its own variables (`${billingUrl}` in `billing.yaml`);
  - `with` passes arguments as before.
- **Imported twice.** The same file under two names gives two sets of names; under the same name, or twice without a name, it is loaded once.
- **Sequences** (spec 0012) name their imports the same way. Their endpoints join the workflow's under the name the sequence gives them. A sequence that imports `billing: ../api/billing.yaml` and a workflow that imports the same file without a name produce `billing.login` and `login`: two names for one endpoint, which is allowed.
- **Duplicates.** A name used for two different files is an error, and so are two endpoints with the same full name. The duplicate error now suggests a name for the import:

  ```text
  orders.yaml:4:5: endpoint 'login' is already defined at api/users.yaml:12:3 (give one import a name: `- billing: ../api/billing.yaml`, then `call: billing.login`)
  ```
- **`tinyprobe call`.** An API file has no imports, so its endpoints keep their names. On a workflow file, `tinyprobe call workflow.yaml billing.create_invoice` works.

## Validation

```text
orders.yaml:4:5: import name 'Billing-API' must be lowercase letters, digits and _, starting with a letter (billing)
orders.yaml:4:5: an import with a name is one `name: path` pair
orders.yaml:5:5: import name 'billing' is already used for ../api/billing.yaml
orders.yaml:12:11: unknown endpoint 'billing.creat_invoice' (did you mean 'billing.create_invoice'?)
```

## Protocol

New keys only, protocol `1`.

- `inspect --json`:
  - each endpoint's `name` is its full name (`billing.create_invoice`);
  - a new key `"import_name"` holds the import's name, or `null`;
  - a new key `"imports"` at the top level lists `{ "name", "file" }` for every import, with `name` `null` for a plain one.
- `step_started` and `step_completed` carry the step's name, which has the prefix when the step has no `name` of its own.

## Extension

Nothing required:

- completion after `call:` offers the names from `inspect --json`, prefixed ones included;
- hover and ▶ Call use the same names;
- the `import` key is completed from the schema.

No new `MINIMUM_VERSION`.

## Docs

- `docs/workflow-format.md`: named imports in the "Workflow file" section; remove namespaced imports from "Not supported yet".
- `docs/llm-guide.md`:
  - several services: one API file per service, each imported with a name;
  - several Postman collections, or several OpenAPI documents, that share endpoint names;
  - the common mistake: renaming endpoints (`billing_login`) to avoid a duplicate.
- `README.md`: the format example.
- `docs/protocol.md`: `import_name` and `imports`.
- JSON Schema: an `import` item is a path or a one-pair mapping.
- `docs/example`: a second small API file of the test server, imported with a name in one workflow.

## Out of scope

- A default name taken from the file name when names clash. It is explicit or nothing: two runs of an agent must give the same names.
- Names for `auth` sets across files. They stay per file, as today.
- `call: billing.*` and other patterns.

## Acceptance criteria

- [x] Named imports as above in workflows and sequences, with every error at its location.
- [x] Plain imports behave exactly as before; the existing workflows and tests pass unchanged.
- [x] Unit tests:
  - prefixed names, and the default step names;
  - the same file under two names, and twice under one;
  - a name used for two files, and an invalid name;
  - the suggestion of the duplicate error, and "did you mean" for a prefixed name;
  - each file keeps its own `base_url` and `auth`;
  - a sequence's named imports;
  - `inspect --json` with `import_name` and `imports`.
- [x] A `docs/example` workflow calls a named import against the test server.
- [x] `scripts/dev.sh check` passes, and the performance guard.

## Open questions

1. **The syntax.** The proposal mixes plain paths and `name: path` pairs in the `import` list, so a workflow adds a name only where it needs one. The alternative is a mapping for every import (`import: { shop: …, billing: … }`): uniform, but it changes how every existing workflow is written. *Recommendation: the list, with optional pairs.*
2. **The separator.** `billing.login`, as the roadmap writes it: short, and read as "login of billing". `/` and `::` read as paths or code. A `.` inside an endpoint name stays allowed, so nothing breaks. *Recommendation: `.`.*
3. **Required names.** Names are optional. Making every import named would remove a choice, but it would break every workflow that exists. *Recommendation: optional.*

## Decisions

Approved on 2026-10-28 with the recommendations:

1. `name: path` pairs, optional, in the `import` list.
2. `.` as the separator: `billing.login`.
3. Names are optional; plain imports keep their names.
