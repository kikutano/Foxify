# Foxify example

A small, complete Foxify project that tests [JSONPlaceholder](https://jsonplaceholder.typicode.com), a free fake REST API. There is no server to start and nothing to install but `foxify`: open it and run.

```text
example/
├── environment.yaml          values for the workflows: the base URL
├── api/
│   └── jsonplaceholder.yaml  the endpoints: what to send, which arguments they take
└── workflows/
    ├── 01-read.yaml          the basics: call, check, save a value for the next step
    ├── 02-write.yaml         POST, PUT, PATCH and DELETE with a body, a generated value
    └── 03-checks.yaml        operators, OR conditions, headers, response time, polling
```

Read them in order: each file is short and commented.

## Run it from the terminal

[Install Foxify](../README.md#install), then from this folder:

```bash
foxify run workflows/01-read.yaml -e environment.yaml
```

Call a single endpoint, with its arguments, and see the whole response:

```bash
foxify call api/jsonplaceholder.yaml get_user --with id=2 -e environment.yaml
```

List the endpoints and their arguments (those without `?` are required):

```bash
foxify inspect api/jsonplaceholder.yaml
```

## Run it in VS Code

1. Install the [Foxify extension](https://github.com/kikutano/foxify-vscode#install).
2. Open this `example` folder in VS Code (**File → Open Folder**).
3. Open a workflow and click **▶ Run workflow** at the top. The first time, pick `environment.yaml` in the list that appears.
4. Open `api/jsonplaceholder.yaml` and click **▶ Call** above an endpoint: it asks for the arguments and shows the response.

Workflows are also in the **Testing** view, one test per step. A mistake in a file is underlined as soon as you save it: try `call: get_usr` in a workflow.

## Next steps

- Change a check, for example `$.name: Leanne Graham` in `01-read.yaml`, and run again to see how a failure is reported.
- Point Foxify at your own API: copy this folder, set `baseUrl` in `environment.yaml` and describe your endpoints in `api/`. A coding agent can write them for you: see [Generate tests with a coding agent](../README.md#generate-tests-with-a-coding-agent).
- The complete format is in [docs/workflow-format.md](../docs/workflow-format.md).
