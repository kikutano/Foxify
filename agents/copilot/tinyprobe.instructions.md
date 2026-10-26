---
applyTo: "tinyprobe/**"
description: tinyprobe API tests (YAML API files and workflows in tinyprobe/)
---

# tinyprobe

tinyprobe tests HTTP APIs with YAML files: API files declare the endpoints once, workflows call them by name, check the responses and save values for the next steps. The format is in the guide that ships with the installed binary. It is the source of truth: read it before you write or change a tinyprobe file, and don't write the syntax from memory.

1. **Read the guide**, all of it, once per task: the `guide` tool of the tinyprobe MCP server if you have it, otherwise `tinyprobe guide`. It matches the installed version.
2. **Set up** if `tinyprobe/` does not exist: `tinyprobe init` (`--base-url http://localhost:8080` for the backend's URL).
3. **Follow the guide's procedure**: section 8 for a backend's code or an OpenAPI document, section 12 for a Postman collection or another tool's export.
4. **Check as you go.** Validate each file with `inspect` (`tinyprobe inspect <file>`), look at real responses with `call` before writing checks, and run workflows with `run` (`tinyprobe run tinyprobe/workflows -e tinyprobe/env.local.yaml`). With the MCP tools, prefer them to the shell.
5. **Done** when the whole folder passes, or the failures left are backend bugs you report to the user.

Rules that matter most; the guide has the others:

- Never invent syntax. What tinyprobe cannot express is skipped and reported, not guessed.
- Credentials and tokens go only in `tinyprobe/env.local.yaml`, which is not committed.
- Never weaken a check to make it pass: a check that fails against the real backend is either a wrong test or a backend bug.

If `tinyprobe` is not installed: `curl -fsSL https://raw.githubusercontent.com/kikutano/tinyprobe/main/install.sh | sh` (Linux, macOS) or `irm https://raw.githubusercontent.com/kikutano/tinyprobe/main/install.ps1 | iex` (Windows). Ask the user before installing.
