# 0005 — `tinyprobe mcp`: tinyprobe as tools for agents

- **Status:** Draft
- **Roadmap:** M2-1
- **Repositories:** tinyprobe
- **PRs:** —

## Problem

Today an agent uses tinyprobe through a shell: it runs `tinyprobe inspect`, `call` and `run`, and reads text made for people. It works, but:

- agents without a shell, or with shell commands that need approval one by one (most editors' chats), cannot check their files at all, or ask the user at every round;
- the console output is meant for people: an agent parses columns and colors, or must remember `--reporter ndjson` and `inspect --json`;
- values on the command line are typed like YAML (`--with id=42`, `--with 'id="42"'`), a common source of mistakes;
- nothing tells an agent that tinyprobe is there until someone pastes a prompt.

[MCP](https://modelcontextprotocol.io) (Model Context Protocol) is how coding agents (Claude Code, Cursor, VS Code and Copilot, Windsurf, Codex and others) discover and call tools. A tinyprobe MCP server makes `guide`, `inspect`, `call` and `run` tools the agent sees and calls directly, with JSON in and JSON out.

## Proposal

```text
$ tinyprobe mcp
```

starts an MCP server on standard input and output. Nobody runs it by hand: the agent's client starts it. The user adds it once:

```bash
claude mcp add tinyprobe -- tinyprobe mcp              # Claude Code
```

```json
// .vscode/mcp.json (VS Code), .cursor/mcp.json (Cursor): { "mcpServers": ... } there
{
  "servers": {
    "tinyprobe": { "type": "stdio", "command": "tinyprobe", "args": ["mcp"] }
  }
}
```

### Tools

| Tool | Arguments | Returns |
|---|---|---|
| `guide` | none | the guide for agents (`tinyprobe guide`), as text |
| `inspect` | `file` | the JSON of `tinyprobe inspect --json`: what the file defines, or its problems |
| `call` | `file`, `endpoint`, `with`?, `env_file`?, `vars`? | the request and the full response of one call, whatever the status |
| `run` | `paths`, `env_file`?, `vars`? | for each workflow: success, summary, and every step with its checks; request and response only for failed steps |

- **Paths** are relative to the folder the client started the server in (the workspace), or absolute.
- **`with` and `vars` are JSON objects**, so values keep their JSON type: `{ "id": 42 }` is a number, `{ "id": "42" }` a string. No YAML typing as on the command line. A string can use variables, as in a step: `{ "id": "${userId}" }`.
- **`env_file`** is the environment file (`-e`). `vars` are set over it (`--var`).
- **`run`** takes one or more paths, files or folders, like `tinyprobe run` (spec 0003): each workflow starts from zero, a failing one does not stop the others.

Example of a `call` result (the text content of the tool result):

```json
{
  "success": true,
  "endpoint": "get_user",
  "request": { "method": "GET", "url": "http://localhost:5122/api/users/1", "headers": [...], "body": null },
  "response": { "status": 200, "reason": "OK", "headers": [...], "body": "{\"id\":1,\"firstName\":\"Gaetano\"}", "body_truncated": false },
  "duration_ms": 4.2
}
```

Example of a `run` result:

```json
{
  "success": false,
  "workflows": [
    {
      "file": "/home/me/p/tinyprobe/workflows/users.yaml",
      "workflow": "Users",
      "success": false,
      "summary": { "total_steps": 12, "passed_steps": 3, "failed_steps": 1, "not_run_steps": 8, "duration_ms": 120.5 },
      "steps": [
        { "index": 0, "name": "Login", "success": true, "status": 200, "duration_ms": 12.4, "saved": { "token": "tinyprobe-test-token" } },
        { "index": 3, "name": "Create a user", "success": false, "status": 500, "failure": "Expected status 201, got 500",
          "assertions": [], "request": { ... }, "response": { ... } }
      ]
    }
  ]
}
```

The keys are those of `step_completed` and `run_finished` in the [protocol](../protocol.md). The difference is that passing steps leave out `request` and `response`, so a passing run stays short in the agent's context.

### Errors

- **An invalid file** is a tool result with `isError: true` and the `invalid` JSON of the protocol: every problem with `file:line:column`, the message and the "did you mean". The agent fixes the file and calls again. Nothing is sent.
- **A failing step** is not an error: the result has `success: false`, and the agent reads why.
- **A wrong argument** (no `file`, a `with` that is not an object, a file that does not exist) is a tool result with `isError: true` and one sentence, for example `File not found: tinyprobe/api/backend.yml`.
- **JSON-RPC errors** are only for the protocol itself: an unknown method (`-32601`), a malformed request (`-32600`), an unknown tool (`-32602`).

### Rules

- **The same engine.** The tools run the same loader, engine and protocol JSON as the CLI, in the same process. Nothing is reimplemented.
- **Standard output belongs to MCP.** Nothing else is written there; logs go to standard error.
- **Stateless.** Each tool call loads the files again and starts from zero: the server keeps nothing between calls ([decision 0003](../decisions/0003-endpoints-declare-workflows-run.md)). An agent that edits a file sees the change at the next call.
- **Tool annotations.** `guide` and `inspect` are read-only (`readOnlyHint`). `call` and `run` send requests to the backend (`openWorldHint`), so clients can ask the user before running them.
- **Protocol version.** The server answers `initialize` with the client's protocol version when it supports it (`2025-06-18`, `2025-03-26`, `2024-11-05`), otherwise with the latest it supports. It declares the `tools` capability only.
- **Native AOT.** JSON-RPC is read with `JsonDocument` and written with `Utf8JsonWriter`, like `ProtocolJson`: no reflection.

## Validation

The server never exits on a bad request: it answers with an error and waits for the next one. It exits when standard input closes.

```text
$ tinyprobe mcp --verbose
Error: Unknown option '--verbose' for 'mcp'.
```

## Protocol

No change to `inspect --json` or the ndjson events. The tool results reuse their keys; `docs/protocol.md` gets a short "MCP" section that lists the tools and the shape of the `call` and `run` results.

## Extension

No change needed. Later, a separate task: the extension registers the server with VS Code (`vscode.lm.registerMcpServerDefinitionProvider`), so Copilot sees the tinyprobe tools without any configuration (new task M2-5).

## Docs

- `README.md`: a "Use tinyprobe from your agent (MCP)" section with the configuration for Claude Code, VS Code and Cursor.
- `docs/llm-guide.md` section 1: "If the tinyprobe MCP tools are available, use them instead of the shell: `inspect`, `call`, `run`, with JSON arguments."
- `tinyprobe init` (spec 0004): the `AGENTS.md` section mentions the MCP server.
- `docs/protocol.md`: the MCP section.

## Out of scope

- An `init` tool, and tools that write files: agents already write files with their own tools.
- MCP resources and prompts (the guide could also be a resource), and the HTTP transport. We add them if clients need them.
- Asking the user before a `run` (elicitation): clients already do it with the annotations.

## Acceptance criteria

- [ ] `tinyprobe mcp` answers `initialize`, `tools/list` and `tools/call` for the four tools, on the Native AOT binary.
- [ ] Unit tests feed JSON-RPC lines to the server and assert the answers: each tool, an invalid file, a failing step, a wrong argument, an unknown method and tool, a notification (no answer).
- [ ] `scripts/dev.sh e2e` and CI pipe a short session (`initialize`, `tools/list`, `run` on `docs/example/workflows`) into `tinyprobe mcp` and check the answers with `jq`.
- [ ] Tried by hand with Claude Code against the test server: the agent finds the tools and converts the Postman bench using them.
- [ ] `scripts/dev.sh check` passes.

## Open questions

1. **The shape of `run` results.** Recommended: request and response only for failed steps, as above, so a passing suite stays short. The alternative is every step in full, as in the ndjson events, which can run into megabytes of response bodies.
2. **Structured content.** Recommended: return the JSON as text content, which every client reads; add `structuredContent` with an `outputSchema` later, when the clients we target use it. The alternative is both from the start.
3. **Tool names.** Recommended: `guide`, `inspect`, `call`, `run`. Clients show them as `tinyprobe` + name (`mcp__tinyprobe__run`). The alternative is prefixed names (`tinyprobe_run`), for clients that mix the tools of many servers into one list.
