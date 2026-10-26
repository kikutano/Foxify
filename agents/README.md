# tinyprobe for coding agents

Ready-made instructions that send your agent to the guide of the installed binary (`tinyprobe guide`) before it writes tinyprobe files. They are short on purpose: the guide is the reference, and it always matches your version.

`tinyprobe init` already adds a section to `AGENTS.md`, which Codex, Cursor, Copilot and others read. Use these packages for the agents and editors that read their own files.

## Claude Code

A plugin with a skill and the tinyprobe MCP server (`guide`, `inspect`, `call`, `run`):

```text
/plugin install tinyprobe --marketplace kikutano/tinyprobe
```

With Claude Code before 2.1.275, in two steps: `/plugin marketplace add kikutano/tinyprobe`, then `/plugin install tinyprobe@tinyprobe`.

Only the MCP server, without the plugin: `claude mcp add tinyprobe -- tinyprobe mcp`.

## Cursor

A rule that applies to the files in `tinyprobe/`:

```bash
curl -fsSL https://raw.githubusercontent.com/kikutano/tinyprobe/main/agents/cursor/tinyprobe.mdc --create-dirs -o .cursor/rules/tinyprobe.mdc
```

The MCP server: `.cursor/mcp.json` with `{ "mcpServers": { "tinyprobe": { "command": "tinyprobe", "args": ["mcp"] } } }`.

## GitHub Copilot

Instructions for the files in `tinyprobe/`:

```bash
curl -fsSL https://raw.githubusercontent.com/kikutano/tinyprobe/main/agents/copilot/tinyprobe.instructions.md --create-dirs -o .github/instructions/tinyprobe.instructions.md
```

In VS Code, the [tinyprobe extension](https://github.com/kikutano/tinyprobe-vscode) gives Copilot's chat the MCP tools with nothing to configure.

## Other agents and tools

[`llms.txt`](../llms.txt) at the root of the repository lists the documents an agent needs, with links to their raw text.

## Keeping them in sync

The three files share the same text after their front matter; a test (`AgentPackageTests`) fails when they differ, or when they stop sending agents to `tinyprobe guide`. Change `claude/skills/tinyprobe/SKILL.md` first, then copy its text to the others.
