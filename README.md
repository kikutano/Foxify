<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="media/logo-dark.svg">
    <img src="media/logo-light.svg" alt="tinyprobe logo: a shell prompt followed by a check mark" width="124">
  </picture>
</p>

# tinyprobe

[![CI](https://github.com/kikutano/tinyprobe/actions/workflows/ci.yml/badge.svg)](https://github.com/kikutano/tinyprobe/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/kikutano/tinyprobe?include_prereleases)](https://github.com/kikutano/tinyprobe/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](License.md)
[![VS Code extension](https://img.shields.io/github/v/release/kikutano/tinyprobe-vscode?label=VS%20Code%20extension&color=orange)](https://github.com/kikutano/tinyprobe-vscode)

**[Install](#install)** · **[VS Code extension](#vs-code-extension)** · **[Try the example](#try-the-example)** · [Workflow format](docs/workflow-format.md) · [Guide for coding agents](docs/llm-guide.md) · [Roadmap](ROADMAP.md)

**Tired of paying for Postman?** Try tinyprobe: fast, light, and your favorite AI writes the endpoints and the end-to-end tests of your API, then runs them against your environment. Describe test flows in YAML files and make sure your API never breaks again.

**Moving from Postman?** Your coding agent converts the collection and tells you what it could not convert: [one prompt](#from-a-postman-collection).

tinyprobe is API testing from the command line. Describe your endpoints once in YAML, then write workflows that call them like functions, pass arguments and check the responses. tinyprobe is a single Native AOT binary: no runtime to install, and it starts instantly.

<p align="center">
  <img src="screen.png" alt="tinyprobe run output in the terminal" width="640">
</p>

## Install

**Linux / macOS**

```bash
curl -fsSL https://raw.githubusercontent.com/kikutano/tinyprobe/main/install.sh | sh
```

**Windows (PowerShell)**

```powershell
irm https://raw.githubusercontent.com/kikutano/tinyprobe/main/install.ps1 | iex
```

Or download the archive for your platform from [Releases](https://github.com/kikutano/tinyprobe/releases) (`win-x64`, `linux-x64`, `linux-arm64`, `osx-arm64`) and put `tinyprobe` on your `PATH`.

Check that it works:

```bash
tinyprobe --version
```

### Update

```bash
tinyprobe update
```

It downloads the latest release for your platform, verifies its SHA-256 checksum and replaces the binary in place. `tinyprobe update --check` only tells you whether a newer version exists. Re-running the install script works too, and `TINYPROBE_VERSION=0.10.0` pins a specific version.

## VS Code extension

[**tinyprobe for VS Code**](https://github.com/kikutano/tinyprobe-vscode) runs the same files from the editor:

<p align="center">
  <a href="https://github.com/kikutano/tinyprobe-vscode">
    <img src="https://raw.githubusercontent.com/kikutano/tinyprobe-vscode/main/media/screen.png" alt="A tinyprobe workflow open in VS Code: a Run workflow link above it, a green check next to each step, and the run view with the steps, the checks of the selected step and its response" width="800">
  </a>
</p>

- problems underlined as you save, with the suggestions of `tinyprobe`;
- **▶ Call** above every endpoint: it asks for the arguments and shows the response;
- **▶ Run workflow**, and workflows in the Test Explorer, one test per step;
- the request and response of every step, and the environment file picked in the status bar.

Install it from the [VS Code Marketplace](https://marketplace.visualstudio.com/items?itemName=kikutano.tinyprobe-vscode), or [Open VSX](https://open-vsx.org/extension/kikutano/tinyprobe-vscode) for Cursor, Windsurf and VSCodium, or from a terminal:

```bash
code --install-extension kikutano.tinyprobe-vscode
```

It needs `tinyprobe` installed as above.

## Try the example

[example/](example/) is a small, complete project that tests [JSONPlaceholder](https://jsonplaceholder.typicode.com), a free fake REST API: nothing to start, nothing else to install. Three short, commented workflows show reads, writes and every kind of check.

```bash
git clone https://github.com/kikutano/tinyprobe.git
cd tinyprobe/example
tinyprobe run workflows/01-read.yaml -e environment.yaml
```

Or open the `example` folder in VS Code and click **▶ Run workflow**. See [example/README.md](example/README.md).

## Quick start

`hello.yaml`:

```yaml
name: Hello tinyprobe
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
tinyprobe run hello.yaml
```

```text
  ⚡ tinyprobe 0.10.0

  workflow : Hello tinyprobe
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

Let a coding agent (Claude Code, Cursor, Copilot, Codex, ...) write the tinyprobe files for you, from your backend's code, a Postman collection or an OpenAPI document. Give it [`docs/llm-guide.md`](docs/llm-guide.md): the format, the rules that are easy to get wrong, a step-by-step procedure and complete examples. Then paste one of the prompts below.

`tinyprobe guide` prints the same guide from the installed binary, so it always matches your version, also offline. If the agent cannot open links, paste the file into the chat.

The agent can check its own work: an invalid file is reported with `file:line:column` and a suggestion before any request is sent, and a failing check shows the actual value.

### From the backend code

```text
Read https://raw.githubusercontent.com/kikutano/tinyprobe/main/docs/llm-guide.md (or run `tinyprobe guide`)
and follow it: look at this backend's routes, authentication and seed data, write the tinyprobe API
file and workflows in tinyprobe/, then run them with tinyprobe until they pass. Put credentials only in
tinyprobe/env.local.yaml and add it to .gitignore. Tell me which failures look like backend bugs.
```

### From a Postman collection

In Postman, export the collection (Collection v2.1) and each environment, and put the files in the repository, for example in `postman/`. Then:

```text
Read https://raw.githubusercontent.com/kikutano/tinyprobe/main/docs/llm-guide.md (or run `tinyprobe guide`)
and follow its section on migrating from Postman: convert the collection and the environments in postman/
to tinyprobe files in tinyprobe/, one folder at a time, and run each workflow until it passes. Put secrets
only in tinyprobe/env.local.yaml and add it to .gitignore. Write tinyprobe/MIGRATION.md with what you
converted, what you skipped and why, and tell me which failures look like backend bugs.
```

Variants of the same request ("Login", "Login - wrong password") become one endpoint called with different arguments, and the values your scripts saved become `save`. What has no equivalent yet (scripts that compute a signature, `pm.sendRequest`, form-data uploads, cookies) is not guessed: the agent lists it in `MIGRATION.md`, so you know what the tests do not cover. The same prompt works for the exports of Insomnia, Bruno or another API tool: name the folder and the tool.

### From an OpenAPI document

```text
Read https://raw.githubusercontent.com/kikutano/tinyprobe/main/docs/llm-guide.md (or run `tinyprobe guide`)
and follow it, starting from the OpenAPI document openapi.yaml: write one tinyprobe endpoint per operation
and workflows that check the documented status codes and response fields, then run them against the
local backend until they pass. Take test values from the seed data, not from the examples in the
document. Put credentials only in tinyprobe/env.local.yaml and add it to .gitignore. Tell me where the
backend does not do what the document says.
```

## Usage

```text
  tinyprobe run <workflow.yaml | folder>... [options]
  tinyprobe call <file.yaml> <endpoint> [--with name=value]... [options]
  tinyprobe inspect <file.yaml> [--json]
  tinyprobe update [--check] [--force]
  tinyprobe guide       Print the guide for coding agents (LLMs) that write tinyprobe files.
  tinyprobe --help
  tinyprobe --version

'run' runs workflows. A folder is read recursively: every YAML file with a top-level
'steps' key is a workflow, other files are skipped. With several workflows, each one
starts from zero, a failing one does not stop the others, and a summary comes last.
'call' sends one request to an endpoint of an API or workflow file and prints the response.
'inspect' validates a file and lists its endpoints, variables and steps.

Options for 'run' and 'call':
  -e, --env <file>        Environment file (YAML) with initial variables, e.g. baseUrl, apiKey.
  --var <name=value>      Set a variable, over the environment file. Repeatable.
  --with <name=value>     ('call' only) An argument of the endpoint, like a step's 'with'. Repeatable.
  --reporter <name>       console (default) or ndjson: one JSON event per line, for tools.
  -v, --verbose           Print variables, requests and responses while running.
  --no-color              Disable colors (also honored: the NO_COLOR environment variable).

Values of --var and --with are typed like YAML: 42 is a number, true a boolean,
'"42"' a string, '[a, b]' a list.

Options for 'inspect':
  --json                  Print JSON, for tools.
  --no-color              Disable colors.

Options for 'update':
  --check                 Only tell whether a newer version is available.
  --force                 Reinstall the latest version, also over a development build.

  -h, --help              Show this help.
  --version               Show the version.

Exit codes ('run', 'call', 'inspect'):
  0  all steps passed ('call': a response was received; 'inspect': the file is valid)
  1  at least one step failed (several workflows: at least one workflow failed)
  2  invalid arguments, file not found, invalid file, or a folder without workflows
```

A workflow stops at its first failing step. Given a folder or several files, `tinyprobe run` runs every workflow, also after a failing one, and ends with one summary of all of them; exit code `0` means every workflow passed:

```bash
tinyprobe run tinyprobe/workflows -e tinyprobe/env.local.yaml
```

A folder is read recursively, and only workflows run: API files and env files in it are skipped. Each workflow starts from zero, so values saved in one are not seen by the next. The exit code makes tinyprobe easy to use in CI. When the output is not a terminal (CI logs, pipes) the spinner is turned off and each step is printed on its own line.

### Explore an API

`inspect` lists what a file defines, and `call` sends a single request and prints the whole response, without writing a workflow:

```bash
tinyprobe inspect docs/example/api/testserver.yaml
tinyprobe call docs/example/api/testserver.yaml status --with code=404 -e docs/environment.yaml
```

`call` accepts any status, so a `404` is shown, not reported as a failure. Arguments are validated like a step's `with`: a missing required param or a misspelled name is an error.

### Use tinyprobe from other tools

`inspect --json` and `run --reporter ndjson` (also `call`) print JSON for programs: editors, CI dashboards, a local UI. The format is versioned and documented in [docs/protocol.md](docs/protocol.md).

```bash
tinyprobe run docs/example/workflows/users.yaml -e docs/environment.yaml --var baseUrl=http://127.0.0.1:5122/ --reporter ndjson
```

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

## Test server example

[docs/example/](docs/example/) tests the test server of this repository, and is what CI runs: one API file and four workflows covering authentication, CRUD, nested JSON, query strings, headers, status codes and slow responses. It needs the .NET SDK to start the server; for a first look, [example/](example/) needs nothing.

```bash
# terminal 1: start the test server on http://localhost:5122
dotnet run --project tests/Tinyprobe.RealServer.Tests
```

```bash
# terminal 2: run a workflow, or all of them
tinyprobe run docs/example/workflows/users.yaml -e docs/environment.yaml
tinyprobe run docs/example/workflows -e docs/environment.yaml
```

The workflows leave the server's data as they found it, so they can be run again without restarting it.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Native AOT also needs the platform toolchain: the "Desktop development with C++" workload on Windows, `clang` and `zlib1g-dev` on Linux, Xcode command line tools on macOS.

```bash
git clone https://github.com/kikutano/tinyprobe.git
cd tinyprobe
dotnet test Tinyprobe.slnx

# run without installing
dotnet run --project src/Tinyprobe.Cli -- run docs/example/workflows/users.yaml -e docs/environment.yaml

# build the native binary (use your RID: win-x64, linux-x64, linux-arm64, osx-arm64)
dotnet publish src/Tinyprobe.Cli/Tinyprobe.Cli.csproj -c Release -r linux-x64 -o publish
```

## Releasing

Push a SemVer tag. The [Release workflow](.github/workflows/release.yml) runs the tests, builds the native binaries for every platform and publishes a GitHub Release with checksums and generated notes.

To put your own notes on top of the generated ones (a breaking change, highlights), write them in an annotated tag. The first line is the title; everything after it goes into the release notes:

```markdown
tinyprobe 0.10.0

## Breaking change

Describe what users must change.
```

```bash
git tag -a v0.10.0 --cleanup=whitespace -F notes.md
```

```bash
git push origin v0.10.0
```

`--cleanup=whitespace` keeps Markdown headings: by default git drops lines that start with `#`. A plain `git tag v0.10.0` works too and publishes only the generated notes. Tags with a suffix, like `v0.11.0-beta.1`, are published as pre-releases.

## Roadmap

See [ROADMAP.md](ROADMAP.md).

## License

MIT. See [License.md](License.md).
