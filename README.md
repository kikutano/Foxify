![](logo.jpg)

# ⚡ Foxify

[![CI](https://github.com/kikutano/foxify/actions/workflows/ci.yml/badge.svg)](https://github.com/kikutano/foxify/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/kikutano/foxify?include_prereleases)](https://github.com/kikutano/foxify/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](License.md)

API testing from the command line. Describe HTTP calls, variable extraction and assertions in a YAML file, and run them with one command. Foxify is a single Native AOT binary: no runtime to install, and it starts instantly.

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

## Quick start

`env.yaml`:

```yaml
baseUrl: "https://jsonplaceholder.typicode.com"
```

`hello.yaml`:

```yaml
metadata:
  name: "Hello Foxify"

functions:
  CreatePost:
    type: HTTP_REQUEST
    method: POST
    endpoint: "/posts"
    headers:
      Content-Type: application/json
    body: |
      { "title": "foxify", "body": "hello", "userId": 1 }
    expected:
      status_code: 201
    extract:
      postId: $.id            # save a value from the JSON response
    asserts:
      postId: "== 101"        # check an extracted value

  GetUser:
    type: HTTP_REQUEST
    method: GET
    endpoint: "/users/1"
    expected:
      status_code: 200
    extract:
      email: $.email
    asserts:
      email: "== 'Sincere@april.biz'"

workflow:
  - type: function
    function_name: CreatePost
  - type: function
    function_name: GetUser
    depends_on: [CreatePost]
```

Run it:

```bash
foxify run hello.yaml -e env.yaml
```

```text
Workflow: Hello Foxify
  PASS  CreatePost (467 ms)
  PASS  GetUser (45 ms)
2 passed, 0 failed, 512 ms
```

## Usage

```text
foxify run <workflow.yaml> [options]
foxify --help
foxify --version

Options:
  -e, --env <file>   Environment file (YAML) with initial variables, e.g. baseUrl, apiKey.
  -v, --verbose      Print the variables loaded before execution.
  -h, --help         Show this help.
  --version          Show the version.

Exit codes:
  0  all steps passed
  1  at least one step failed
  2  invalid arguments or file not found
```

Execution stops at the first failing step. The exit code makes Foxify easy to use in CI.

## Workflow file

| Key | Description |
|---|---|
| `metadata.name` | Name shown in the report. |
| `functions.<Name>` | A reusable HTTP request: `method`, `baseurl` (optional, defaults to `baseUrl` from the env file), `endpoint`, `headers`, `body`. |
| `expected.status_code` | Expected HTTP status code. |
| `extract` | `variable: <JSONPath>` pairs, e.g. `$.token`, `$.data[0].id`, `$.user.address.city`. |
| `asserts` | Checks on extracted variables: `==`, `!=`, `>`, `>=`, `<`, `<=`, combinable with `&&` and `\|\|`. |
| `workflow` | Steps run in order: `type: function` with `function_name` (and optional `depends_on`), or `type: DELAY` with `duration_seconds`. |

Use `${name}` anywhere in `baseurl`, `endpoint`, `headers` or `body` to reference variables from the env file or values extracted by earlier steps, e.g. `Authorization: "Bearer ${token}"`.

More details: [docs/variable_extraction_guide.md](docs/variable_extraction_guide.md).

## Full example

[docs/full_example.yaml](docs/full_example.yaml) covers authentication, CRUD, nested JSON, headers, query strings, status codes and delays against the bundled test server:

```bash
# terminal 1: start the test server on http://localhost:5122
dotnet run --project tests/Foxify.RealServer.Tests
```

```bash
# terminal 2: run the workflow
foxify run docs/full_example.yaml -e docs/environment.yaml
```

The workflow changes the server's in-memory data (it deletes a user), so restart the server before running it again.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Native AOT also needs the platform toolchain: the "Desktop development with C++" workload on Windows, `clang` and `zlib1g-dev` on Linux, Xcode command line tools on macOS.

```bash
git clone https://github.com/kikutano/foxify.git
cd foxify
dotnet test Foxify.slnx

# run without installing
dotnet run --project src/Foxify.Cli -- run docs/full_example.yaml -e docs/environment.yaml

# build the native binary (use your RID: win-x64, linux-x64, linux-arm64, osx-arm64)
dotnet publish src/Foxify.Cli/Foxify.Cli.csproj -c Release -r linux-x64 -o publish
```

## Releasing

Push a SemVer tag. The [Release workflow](.github/workflows/release.yml) runs the tests, builds the native binaries for every platform and publishes a GitHub Release with checksums and generated notes:

```bash
git tag v0.1.0
git push origin v0.1.0
```

Tags with a suffix, like `v0.2.0-beta.1`, are published as pre-releases.

## Roadmap

See [ROADMAP.md](ROADMAP.md).

## License

MIT. See [License.md](License.md).
