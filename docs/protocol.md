# tinyprobe JSON protocol

Programs (a local UI, an editor plugin, a CI dashboard) drive tinyprobe through the `tinyprobe` binary and read JSON from its standard output. They never parse YAML or reimplement checks: the binary is the only engine.

| Command | Output |
|---|---|
| `tinyprobe inspect <file.yaml> --json` | one JSON document: what the file defines, or its problems |
| `tinyprobe run <workflow.yaml> --reporter ndjson` | one JSON event per line, as the run goes |
| `tinyprobe call <file.yaml> <endpoint> --reporter ndjson` | the same events, for a single request |

## Rules

- **Versioning.** Every document and the first event of a run carry `"protocol": 1` and `"version"`, the tinyprobe version. The protocol number changes only when a key is removed or changes meaning. New keys can appear in any release, so ignore keys you don't know.
- **Keys** are `snake_case`. **Durations** are milliseconds (`*_ms`), as numbers.
- **YAML values** (variables, bodies, `with`, param defaults) are shown as JSON with their placeholders **unresolved**: `"${email}"`, `"smoke-${$uuid}@example.com"`. Only a run resolves them.
- **Locations** are `{ "file", "line", "column" }`, 1-based. `file` is relative to the current directory when the file is inside it, otherwise absolute. Problems that concern a whole file have `line` 0. Problems with arguments given on the command line have the file `command line`.
- **Exit codes** are the same as for people: `0` success, `1` a step failed, `2` invalid arguments or invalid file. Errors in the arguments themselves (unknown option, missing file) are plain text on standard error, with exit code `2` and no JSON.
- **Encoding.** The output is UTF-8. Non-ASCII characters are written as `\uXXXX` escapes, so the bytes are plain ASCII.

## `inspect --json`

Takes a workflow or an API file (a file without `steps`). A workflow is validated completely, imports included.

```json
{
  "protocol": 1,
  "version": "0.9.0",
  "valid": true,
  "kind": "workflow",
  "file": "/home/me/project/tinyprobe/workflows/users.yaml",
  "name": "Users",
  "description": "",
  "settings": { "timeout_ms": 30000, "max_retries": 0 },
  "variables": [
    { "name": "email", "value": "smoke-${$uuid}@example.com", "location": { "file": "tinyprobe/workflows/users.yaml", "line": 5, "column": 10 } }
  ],
  "endpoints": [
    {
      "name": "login",
      "method": "POST",
      "base_url": "${baseUrl}",
      "path": "/login",
      "query": [],
      "headers": [ { "name": "X-Client", "value": "tinyprobe" } ],
      "body": { "email": "${email}", "password": "${password}" },
      "params": [
        { "name": "email", "required": true, "default": null },
        { "name": "password", "required": false, "default": "secret" }
      ],
      "arguments": ["baseUrl", "email", "password"],
      "location": { "file": "tinyprobe/api/backend.yaml", "line": 12, "column": 3 }
    }
  ],
  "steps": [
    {
      "index": 0,
      "type": "call",
      "name": "Login",
      "call": "login",
      "with": { "email": "${email}" },
      "expect": {
        "status": [200],
        "checks": [ { "subject": "$.token", "condition": "exists" }, { "subject": "duration", "condition": "< 500ms" } ]
      },
      "until": null,
      "save": { "token": "$.token" },
      "location": { "file": "tinyprobe/workflows/users.yaml", "line": 9, "column": 5 }
    },
    { "index": 1, "type": "sleep", "name": "sleep 1s", "duration_ms": 1000, "location": { "file": "tinyprobe/workflows/users.yaml", "line": 17, "column": 5 } }
  ]
}
```

- `kind` is `workflow` or `api`. An API file has an empty `steps` and `variables`.
- `headers` are the effective headers of the endpoint: file headers, then `auth` sets, then the endpoint's own, where a later one with the same name replaces an earlier one.
- `arguments` are the names a call of the endpoint can pass with `--with` (or a step's `with`): its params, its query names and every placeholder of its base URL, path, headers and body. Generated values (`${$uuid}`) are not listed. A UI can build the form of an endpoint from `params` (required ones first) and `arguments`.
- `until`, when present, has the same `status` and `checks` as `expect`, plus `every_ms` and `timeout_ms`.
- `checks[].condition` is the text the report shows, such as `>= 3`, `in ["a","b"]`, `< 0 or > 100`.

An invalid file gives exit code `2` and:

```json
{
  "protocol": 1,
  "version": "0.9.0",
  "valid": false,
  "file": "/home/me/project/tinyprobe/workflows/users.yaml",
  "errors": [
    {
      "file": "tinyprobe/workflows/users.yaml",
      "line": 14,
      "column": 11,
      "message": "unknown endpoint 'get_usr' (did you mean 'get_user'?)",
      "text": "tinyprobe/workflows/users.yaml:14:11: unknown endpoint 'get_usr' (did you mean 'get_user'?)"
    }
  ]
}
```

## `--reporter ndjson`

One JSON object per line on standard output, written and flushed as soon as it happens. Every event has an `event` key. With `-v` the request log goes to standard error, so standard output stays valid NDJSON.

| Event | When |
|---|---|
| `run_started` | the workflow is valid and is about to run |
| `step_started` | a step starts, and again before each new attempt of an `until` step |
| `step_completed` | a call step ended, passed or failed (not sent for `sleep` steps) |
| `run_finished` | the run ended; the last event |
| `invalid` | the workflow is invalid: the only event, exit code `2` |

```json
{"event":"run_started","protocol":1,"version":"0.9.0","workflow":"Users","file":"/home/me/project/tinyprobe/workflows/users.yaml","env_file":"/home/me/project/tinyprobe/env.local.yaml","steps":[{"index":0,"type":"call","name":"Login"},{"index":1,"type":"sleep","name":"sleep 1s"}]}
{"event":"step_started","index":0,"type":"call","name":"Login","attempt":1}
{"event":"step_completed","index":0,"name":"Login","success":true,"failure":null,"method":"POST","url":"http://localhost:8080/login","status":200,"expected_status":[200],"duration_ms":12.4,"attempts":1,"bytes_sent":48,"bytes_received":120,"assertions":[{"subject":"$.token","condition":"exists","actual":"\"eyJ...\"","passed":true}],"saved":{"token":"eyJ..."},"request":{"method":"POST","url":"http://localhost:8080/login","headers":[{"name":"Content-Type","value":"application/json; charset=utf-8"}],"body":"{\"email\":\"ada@example.com\",\"password\":\"...\"}"},"response":{"status":200,"reason":"OK","headers":[{"name":"Content-Type","value":"application/json"}],"body":"{\"token\":\"eyJ...\"}","body_truncated":false}}
{"event":"step_started","index":1,"type":"sleep","name":"sleep 1s","attempt":1}
{"event":"run_finished","success":true,"summary":{"total_steps":1,"passed_steps":1,"failed_steps":0,"not_run_steps":0,"checks_passed":2,"checks_failed":0,"http_requests":1,"bytes_sent":48,"bytes_received":120,"duration_ms":1015.2,"http_req_duration_ms":{"avg":12.4,"min":12.4,"med":12.4,"max":12.4,"p90":12.4,"p95":12.4}}}
```

- `index` is the position of the step in the workflow, sleeps included, and matches `steps[].index` of `inspect` and `run_started`.
- `step_completed` of a failed step has `success: false` and either `failure` (a status, network or variable problem) or failed `assertions`. Steps after it are not run: they are counted in `not_run_steps`.
- `status` is `null` when no response was received, for example on a network error. Then `response` is `null` too, while `request` shows what was sent.
- `request` and `response` belong to the last attempt. `headers` keep their order and include content headers. Bodies are text; a response body over 1 MiB is cut and has `body_truncated: true`. Headers set by the HTTP client itself (such as `Host`) are not listed.
- `saved` holds the values stored by `save` in this step, with their JSON type.
- `assertions[].actual` is the actual value as JSON text, or `(missing)`.
- `http_req_duration_ms` is `null` when no request got a response.

**Secrets.** Requests are shown as sent, `Authorization` headers and passwords included. The output is meant for a program on your machine: do not store it or publish it as it is.

## Variables from the command line

`--var name=value` sets a variable over the environment file, for `run` and `call`. It can be repeated. The value is typed like a YAML value: `--var id=42` is a number, `--var 'id="42"'` a string, `--var 'tags=[a, b]'` a list, and `--var name=` an empty string. Placeholders are not resolved, as in an environment file.

A UI that lets people change the variables of a workflow passes them with `--var` and never rewrites the YAML files.

## `call`

`tinyprobe call <file.yaml> <endpoint> [--with name=value]...` runs a workflow of one step that calls the endpoint and accepts **any** status. `<file.yaml>` is an API file or a workflow; with a workflow, its variables and settings apply. `--with` values are typed like `--var`, and they can use variables: `--with 'id=${userId}'`. They are validated like a step's `with`: an unknown endpoint, a missing required param or an unknown argument is an `invalid` event with the file `command line`.
