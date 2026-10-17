<p align="center">
  <img src="logo.jpg" alt="foxify logo">
</p>

# ⚡ Foxify 

[![CI](https://github.com/kikutano/foxify/actions/workflows/ci.yml/badge.svg)](https://github.com/kikutano/foxify/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/kikutano/foxify?include_prereleases)](https://github.com/kikutano/foxify/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](License.md)

Tired of pay for Postman? Try this! Fast, light, use your favorite AI to create endpoints, E2E tests against your environment! Describe testing flows using YAML file and make sure your API will never break again!
Foxify is an API testing from the command line. Describe your endpoints once in YAML, then write workflows that call them like functions, pass arguments and check the responses. Foxify is a single Native AOT binary: no runtime to install, and it starts instantly.

<p align="center">
  <img src="screen.png" alt="foxify run output in the terminal" width="640">
</p>

## Install

**Linux / macOS**

```bash
curl -fsSL https://raw.githubusercontent.com/kikutano/foxify/main/install.sh | sh
```

**Windows (PowerShell)**

```powershell
irm https://raw.githubusercontent.com/kikutano/foxify/main/install.ps1 | iex
```

Or download the archive for your platform from [Releases](https://github.com/kikutano/foxify/releases) (`win-x64`, `linux-x64`, `linux-arm64`, `osx-arm64`) and put `foxify` on your `PATH`.

Check that it works:

```bash
foxify --version
```

### Update

```bash
foxify update
```

It downloads the latest release for your platform, verifies its SHA-256 checksum and replaces the binary in place. `foxify update --check` only tells you whether a newer version exists. Re-running the install script works too, and `FOXIFY_VERSION=0.1.0` pins a specific version.

## Quick start

`hello.yaml`:

```yaml
name: Hello Foxify
base_url: https://jsonplaceholder.typicode.com

endpoints:
  create_post:
    method: POST
    path: /posts
    body:
      title: ${title}
      userId: 1
  get_user:
    path: /users/${id}

steps:
  - name: Create a post
    call: create_post
    with:
      title: hello
    expect:
      status: 201
      body:
        $.id: 101
        $.title: hello
    save:
      postId: $.id              # available as ${postId} in the next steps

  - call: get_user
    with:
      id: 1
    expect:
      body:
        $.email: Sincere@april.biz
        $.address.city: { contains: Gwen }
      duration: { lt: 2s }
```

Run it:

```bash
foxify run hello.yaml
```

```text
  ⚡ foxify 0.4.0

  workflow : Hello Foxify
  file     : hello.yaml
  steps    : 2

  ✓ Create a post  POST   201    708.6ms  /posts
  ✓ get_user id=1  GET    200    33.27ms  /users/1

  checks..............: 100.00%  ✓ 6  ✗ 0
  steps...............: 2/2 passed
  http_reqs...........: 2  2.3/s
  http_req_duration...: avg=370.94ms min=33.27ms med=370.94ms max=708.6ms p(90)=641.07ms p(95)=674.84ms
  data_received.......: 559 B
  data_sent...........: 28 B
  workflow_duration...: 869.39ms

   PASSED   2 steps
```

When a step fails, every check of that step is listed with the actual value, and the steps after it are not run:

```text
  ✗ Wrong expectations  GET    200     4.73ms  /api/headers
      ✗ header X-Custom-Header == "something-else"  actual "hello-world"
      ✓ $.message contains "headers"
      ✗ duration < 1ms  actual 4.73ms
```

A mistake in the YAML is reported before any request is sent, with every problem and its location:

```text
  ✗ The workflow is not valid (2 problems)

  tests/users.yaml:12:5: unknown endpoint 'logn' (did you mean 'login'?)
  tests/users.yaml:16:5: unknown key 'expcet' in a step (did you mean 'expect'?)
```

## Generate tests with a coding agent

Let a coding agent (Claude Code, Cursor, Copilot, Codex, ...) read your backend and write the Foxify files for you. Give it [`docs/llm-guide.md`](docs/llm-guide.md): the format, the rules that are easy to get wrong, a step-by-step procedure and a complete example. Then paste a prompt like this:

```text
Read https://raw.githubusercontent.com/kikutano/foxify/main/docs/llm-guide.md (or run `foxify guide`)
and follow it: look at this backend's routes, authentication and seed data, write the Foxify API
file and workflows in foxify/, then run them with foxify until they pass. Put credentials only in
foxify/env.local.yaml and add it to .gitignore. Tell me which failures look like backend bugs.
```

`foxify guide` prints the same guide from the installed binary, so it always matches your version, also offline. If the agent cannot open links, paste the file into the chat.

The agent can check its own work: an invalid file is reported with `file:line:column` and a suggestion before any request is sent, and a failing check shows the actual value.

## Usage

```text
foxify run <workflow.yaml> [options]
foxify update [--check] [--force]
foxify guide       Print the guide for coding agents (LLMs) that write Foxify files.
foxify --help
foxify --version

Options for 'run':
  -e, --env <file>   Environment file (YAML) with initial variables, e.g. baseUrl, apiKey.
  -v, --verbose      Print variables, requests and responses while running.
  --no-color         Disable colors (also honored: the NO_COLOR environment variable).

Options for 'update':
  --check            Only tell whether a newer version is available.
  --force            Reinstall the latest version, also over a development build.

  -h, --help         Show this help.
  --version          Show the version.

Exit codes ('run'):
  0  all steps passed
  1  at least one step failed
  2  invalid arguments, file not found or invalid workflow
```

Execution stops at the first failing step. The exit code makes Foxify easy to use in CI. When the output is not a terminal (CI logs, pipes) the spinner is turned off and each step is printed on its own line.

## Workflow format

Endpoints and tests live in separate files, so the same endpoints serve many workflows:

```yaml
# api/server.yaml: endpoints only, no expectations
base_url: ${baseUrl}
auth:
  bearer:
    Authorization: "Bearer ${token}"
endpoints:
  login:
    method: POST
    path: /api/login
    params:
      username: admin        # default
      password:              # required
    body:
      username: ${username}
      password: ${password}
  get_user:
    path: /api/users/${id}
    auth: bearer
```

```yaml
# workflows/users.yaml: steps that call the endpoints
name: Users
import: [../api/server.yaml]
steps:
  - call: login
    with: { password: password }
    save: { token: $.access_token }

  - call: get_user
    with: { id: 1 }
    expect:
      status: 200
      headers:
        Content-Type: { contains: json }
      body:
        $.email: gaetano@example.com
        $.orders: { gt: 0, lt: 100 }       # several conditions on one value: all must pass
        $.name: { eq: Gaetano, contains: Gae }
        $.role: { in: [admin, owner] }     # OR: one of these values
        $.deletedAt: { any: [ null, { exists: false } ] }   # OR: one of these conditions

  - sleep: 500ms
```

| In a step | Description |
|---|---|
| `call` / `with` | The endpoint to call and its arguments. |
| `expect.status` | Expected status code, or a list (`[200, 204]`); without it any 2xx is accepted. |
| `expect.body` | Checks by JSONPath: a plain value means equal; operators `eq`, `ne`, `gt`, `gte`, `lt`, `lte`, `exists`, `contains`. Combine them on one value, e.g. `{ gt: 0, lt: 10 }`: all must pass. For OR use `in: [a, b]` (one of these values) or `any: [ { lt: 0 }, { gt: 100 } ]` (one of these conditions). |
| `expect.headers` | Checks on response headers. |
| `expect.duration` | Response time, e.g. `{ lt: 500ms }`. |
| `save` | Values from the response body for the next steps. |
| `until` | Polling: repeat the call until conditions pass, e.g. `until: { body: { $.status: Confirmed }, every: 1s, timeout: 30s }`. |
| `sleep` | A pause, e.g. `sleep: 2s`. |

`${name}` reads the step's arguments, saved values, the environment file (`-e`) and the workflow's `variables`, in this order. Names starting with `$` are generated: `${$uuid}`, `${$timestamp}`, `${$randomString}`, `${$now+2h}`, `${$today+1d}`; define one in `variables` to reuse the same value in every step. The complete reference is in [docs/workflow-format.md](docs/workflow-format.md).

## Full example

[docs/example/](docs/example/) tests the bundled test server: one API file and four workflows covering authentication, CRUD, nested JSON, query strings, headers, status codes and slow responses.

```bash
# terminal 1: start the test server on http://localhost:5122
dotnet run --project tests/Foxify.RealServer.Tests
```

```bash
# terminal 2: run a workflow
foxify run docs/example/workflows/users.yaml -e docs/environment.yaml
```

The workflows leave the server's data as they found it, so they can be run again without restarting it.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Native AOT also needs the platform toolchain: the "Desktop development with C++" workload on Windows, `clang` and `zlib1g-dev` on Linux, Xcode command line tools on macOS.

```bash
git clone https://github.com/kikutano/foxify.git
cd foxify
dotnet test Foxify.slnx

# run without installing
dotnet run --project src/Foxify.Cli -- run docs/example/workflows/users.yaml -e docs/environment.yaml

# build the native binary (use your RID: win-x64, linux-x64, linux-arm64, osx-arm64)
dotnet publish src/Foxify.Cli/Foxify.Cli.csproj -c Release -r linux-x64 -o publish
```

## Releasing

Push a SemVer tag. The [Release workflow](.github/workflows/release.yml) runs the tests, builds the native binaries for every platform and publishes a GitHub Release with checksums and generated notes.

To put your own notes on top of the generated ones (a breaking change, highlights), write them in an annotated tag. The first line is the title; everything after it goes into the release notes:

```markdown
Foxify 0.5.0

## Breaking change

Describe what users must change.
```

```bash
git tag -a v0.5.0 --cleanup=whitespace -F notes.md
```

```bash
git push origin v0.5.0
```

`--cleanup=whitespace` keeps Markdown headings: by default git drops lines that start with `#`. A plain `git tag v0.5.0` works too and publishes only the generated notes. Tags with a suffix, like `v0.6.0-beta.1`, are published as pre-releases.

## Roadmap

See [ROADMAP.md](ROADMAP.md).

## License

MIT. See [License.md](License.md).
