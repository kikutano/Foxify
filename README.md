# Tyfapi

> **Define API scenarios once. Run them anywhere. Let any AI agent generate them.**

Tyfapi is a **Git-native, declarative API scenario runner** designed to make realistic backend verification easy to define, reproduce, automate, and execute across environments.

Tyfapi is **AI-friendly, not AI-powered**.

It does not contain an AI model, does not require an AI provider, and does not lock users into a specific LLM. Developers can use any LLM or coding agent they prefer — or no AI at all — to create and modify Tyfapi YAML files.

The core product is a deterministic execution engine and a simple, machine-readable scenario format.

---

# 1. Vision

API endpoint testing is easy. Testing a realistic journey through an entire backend is harder.

A real user does not interact with one endpoint at a time. They perform sequences such as:

```text
Login
  ↓
Extract authentication token
  ↓
Get profile
  ↓
Search product
  ↓
Create cart
  ↓
Add product
  ↓
Checkout
  ↓
Verify order
```

These scenarios contain state, dependencies, extracted values, authentication, ordering, delays, and environment-specific configuration.

Tyfapi aims to make these scenarios **first-class, version-controlled artifacts**.

The long-term vision is:

> **Define a realistic API scenario once and reliably reuse it everywhere.**

The same scenario should eventually be usable for:

```text
Local development
       ↓
Integration testing
       ↓
CI/CD
       ↓
Staging verification
       ↓
Production smoke testing
       ↓
Load testing
```

---

# 2. The Problem

Backend API testing is often fragmented across:

- manually written integration tests;
- Postman or similar collections;
- shell scripts;
- custom test frameworks;
- CI-specific scripts;
- load-testing tools;
- manually executed requests.

Individual endpoint checks are useful, but they do not always capture how the system is actually used.

For example, testing:

```text
POST /login
GET /users/me
POST /cart
POST /checkout
```

individually does not necessarily verify that:

```text
login → token → cart → checkout
```

works as a complete user journey.

Tyfapi focuses on that missing layer:

> **Scenario-level API verification.**

---

# 3. The Core Idea

Tyfapi is built around three concepts.

## Function

A reusable API operation.

```yaml
Login:
  type: HTTP_REQUEST
  method: POST
  endpoint: /login
  body:
    username: ${username}
    password: ${password}
  extract:
    token: $.token
```

## Flow

A composition of Functions representing a realistic scenario.

```yaml
workflow:
  - type: function
    function_name: Login

  - type: function
    function_name: GetProfile
    depends_on:
      - Login
```

## Environment

The same scenario can run against different targets without changing the scenario itself.

```bash
tyfapi run checkout.yaml --env local
tyfapi run checkout.yaml --env dev
tyfapi run checkout.yaml --env staging
tyfapi run checkout.yaml --env prod
```

The scenario describes **behavior**.

The environment describes **where that behavior is executed**.

---

# 4. What Tyfapi Is

Tyfapi is:

- **Declarative** — scenarios are described as YAML rather than imperative test code.
- **Git-native** — scenarios are ordinary text files that can be versioned, reviewed, diffed, and shared.
- **Deterministic** — the execution engine does not depend on AI.
- **AI-friendly** — the format is intentionally simple and structured so LLMs and coding agents can generate and modify it reliably.
- **Environment-independent** — scenarios should not need to change when the target environment changes.
- **CLI-first** — the execution engine is designed for developers and CI/CD pipelines first.
- **Composable** — reusable Functions can be combined into realistic Flows.
- **Machine-readable** — validation and execution results should be available in structured formats suitable for automation and AI agents.
- **Portable** — the core runner should be distributed as a lightweight standalone executable.
- **Open-source friendly** — the core should be usable without an account, cloud service, or proprietary platform.

---

# 5. What Tyfapi Is NOT

Tyfapi is deliberately **not**:

- an AI assistant;
- an LLM provider;
- a ChatGPT replacement;
- a mandatory cloud platform;
- a browser automation framework;
- a Postman clone;
- a replacement for every load-testing platform;
- a proprietary API testing format that requires a hosted service.

Tyfapi should work completely locally.

A developer should be able to clone a repository, install the CLI, and run:

```bash
tyfapi run ./flows/checkout.yaml --env local
```

without creating an account.

---

# 6. AI-Friendly, Not AI-Powered

This is a fundamental product principle.

Tyfapi does **not** include an AI model.

The user chooses their own AI tooling.

For example:

```text
Claude Code
Codex
Gemini CLI
GitHub Copilot
Local LLM
Future AI Agent
        │
        ▼
   Tyfapi YAML
        │
        ▼
   Tyfapi CLI
        │
        ▼
       API
```

The AI is simply a **consumer and producer of the Tyfapi format**.

This means Tyfapi does not need to:

- host models;
- pay model providers;
- manage AI API keys;
- select an AI provider;
- maintain proprietary prompts;
- compete directly with AI companies;
- change whenever a new LLM becomes popular.

Tyfapi remains useful with zero AI involvement.

---

# 7. Why Make the Format AI-Friendly?

Modern coding agents are increasingly capable of reading repositories, modifying files, executing commands, and iterating based on command results.

Tyfapi should make that workflow particularly easy.

An agent should be able to:

1. Read an OpenAPI specification or existing backend code.
2. Understand the Tyfapi schema.
3. Generate a scenario.
4. Run validation.
5. Execute the scenario.
6. Read structured failures.
7. Modify the YAML.
8. Run it again.

Conceptually:

```text
Developer intent
       │
       ▼
   AI Agent
       │
       ▼
Tyfapi YAML
       │
       ▼
tyfapi validate
       │
       ▼
tyfapi run
       │
       ▼
     API
       │
       ▼
Structured result
       │
       ▼
   AI Agent
       │
       └──────→ modify YAML
```

The important point is:

> **Tyfapi itself remains deterministic.**

---

# 8. YAML as the Source of Truth

The Tyfapi YAML file is the canonical representation of a scenario.

It should be:

- readable by humans;
- writable by humans;
- writable by AI agents;
- easy to diff;
- easy to review;
- easy to store in Git;
- independent of the UI;
- independent of the cloud.

A visual editor, if introduced later, must operate **around the YAML**, not replace it.

The intended relationship is:

```text
             Tyfapi YAML
                  │
        ┌─────────┴─────────┐
        ▼                   ▼
     CLI/Core            Visual UI
        │                   │
        └─────────┬─────────┘
                  ▼
            Same scenario
```

The YAML remains the source of truth.

---

# 9. Example

A minimal scenario:

```yaml
metadata:
  name: "Login & Profile"
  api_version: "v1"

functions:

  LoginUser:
    type: HTTP_REQUEST
    method: POST
    baseurl: ${baseUrl}
    endpoint: /login

    headers:
      Content-Type: application/json

    body:
      username: ${username}
      password: ${password}

    extract:
      token: $.token

  GetProfile:
    type: HTTP_REQUEST
    method: GET
    baseurl: ${baseUrl}
    endpoint: /me

    headers:
      Authorization: "Bearer ${token}"

workflow:

  - type: function
    function_name: LoginUser

  - type: DELAY
    duration_seconds: 1

  - type: function
    function_name: GetProfile
    depends_on:
      - LoginUser

settings:
  timeout: 10
  max_retries: 3
```

Run it:

```bash
tyfapi validate ./login.yaml
tyfapi run ./login.yaml --env dev
```

---

# 10. Variables

Variables allow scenarios to pass state between requests.

For example:

```yaml
LoginUser:
  type: HTTP_REQUEST
  method: POST
  endpoint: /login

  extract:
    token: $.token
    user_id: $.user.id
    username: $.user.username
```

The extracted variables can then be used by later steps:

```yaml
GetProfile:
  type: HTTP_REQUEST
  method: GET
  endpoint: /users/${user_id}

  headers:
    Authorization: "Bearer ${token}"
```

Initial JSONPath support should include:

```text
$.token
$.user.id
$.user.username
$.items[0].name
```

The exact syntax may evolve, but the principle should remain simple:

> **Capture values from one step and make them available to later steps.**

---

# 11. Dependencies

Flows should explicitly describe relationships between steps.

```yaml
workflow:

  - type: function
    function_name: Login

  - type: function
    function_name: CreateCart
    depends_on:
      - Login

  - type: function
    function_name: AddProduct
    depends_on:
      - CreateCart
```

This expresses:

```text
Login
  ↓
CreateCart
  ↓
AddProduct
```

The initial implementation should prioritize predictable sequential execution.

More advanced dependency graphs and controlled parallelism can be introduced later if real use cases require them.

---

# 12. Environment Independence

A core design goal is:

> **The scenario describes behavior, not infrastructure.**

For example:

```yaml
baseurl: ${baseUrl}
```

Environment configuration provides the actual value:

```yaml
local:
  baseUrl: http://localhost:5000

dev:
  baseUrl: https://api.dev.example.com

staging:
  baseUrl: https://api.staging.example.com

prod:
  baseUrl: https://api.example.com
```

The scenario remains unchanged.

This makes the same Flow usable across:

```text
local → CI → dev → staging → production
```

Secrets should never be committed directly into scenario files.

---

# 13. Machine-Readable Results

Machine-readable output is a first-class requirement.

Human-readable output:

```text
✓ LoginUser              182ms
✓ GetProfile              43ms

2/2 steps passed
Total: 225ms
```

Machine-readable output:

```bash
tyfapi run checkout.yaml --format json
```

could produce:

```json
{
  "status": "failed",
  "flow": "checkout",
  "steps": [
    {
      "name": "Login",
      "status": "passed",
      "duration_ms": 182
    },
    {
      "name": "Checkout",
      "status": "failed",
      "duration_ms": 241,
      "response_status": 500
    }
  ]
}
```

The exact schema will be defined during development.

The requirement is:

> **An AI agent or CI pipeline must be able to understand the result without parsing human-oriented terminal output.**

This is important both for automation and for the AI-friendly philosophy.

---

# 14. CLI

The CLI is the first and most important interface.

Initial commands:

## Validate

```bash
tyfapi validate ./flows/checkout.yaml
```

## Run

```bash
tyfapi run ./flows/checkout.yaml --env dev
```

## Machine-readable run

```bash
tyfapi run ./flows/checkout.yaml --env dev --format json
```

Future capability:

```bash
tyfapi run ./flows --env staging
```

Future load testing:

```bash
tyfapi load ./flows/checkout.yaml --bots 1000
```

The CLI must provide real value even if the user never installs a VS Code extension or uses the cloud platform.

---

# 15. Technology

The initial implementation uses:

- **C# / .NET**
- **Native AOT**
- `HttpClient`
- `SocketsHttpHandler`
- source-generated serialization where appropriate
- an AOT-compatible YAML parser

The objectives are:

- fast startup;
- small footprint;
- low operational overhead;
- simple distribution;
- no external runtime dependency;
- suitability for CI/CD;
- efficient HTTP execution.

Native AOT is an implementation choice, not the primary product value proposition.

The value proposition is:

> **Run the same API scenario reliably anywhere.**

---

# 16. MVP — The First Release

The first release must be deliberately small.

The goal is **not** to build the complete Tyfapi platform.

The goal is to validate one hypothesis:

> **Do developers find it useful to describe realistic API journeys in YAML, version them with Git, and run them repeatedly across environments?**

The first MVP is therefore a:

## Scenario Runner

```text
Tyfapi YAML
     ↓
Tyfapi CLI
     ↓
Real API
     ↓
Structured result
```

---

# 17. MVP Scope

The MVP should include only the following capabilities.

### HTTP

```text
GET
POST
PUT
PATCH
DELETE
```

### Requests

- URL/base URL;
- headers;
- query parameters;
- request body.

### Variables

```text
${token}
${userId}
${baseUrl}
```

### Response extraction

```text
$.token
$.user.id
$.items[0].id
```

### Flows

Ordered execution:

```text
A → B → C → D
```

### Dependencies

```yaml
depends_on:
  - Login
```

### Delays

```yaml
- type: DELAY
  duration_seconds: 1
```

### Environment configuration

Separate environment files.

### Validation

```bash
tyfapi validate scenario.yaml
```

### Execution

```bash
tyfapi run scenario.yaml --env dev
```

### Exit codes

```text
0 = success
1 = failure
```

### Output

- human-readable terminal output;
- machine-readable JSON output.

---

# 18. What the MVP Does NOT Include

The first release should explicitly avoid:

- AI integration;
- AI API calls;
- proprietary AI agents;
- SaaS;
- user accounts;
- database;
- cloud dashboard;
- VS Code extension;
- visual editor;
- distributed load testing;
- multi-region infrastructure;
- advanced analytics;
- enterprise authentication;
- complex plugin systems.

These are potential future features, not MVP requirements.

---

# 19. MVP Example Scenarios

The MVP should ship with at least three compelling examples.

## Authentication

```text
POST /login
     ↓
extract token
     ↓
GET /me
```

Demonstrates:

- variables;
- extraction;
- dependencies;
- authentication headers.

## CRUD

```text
Create
  ↓
Read
  ↓
Update
  ↓
Delete
```

Demonstrates:

- resource IDs;
- variable propagation;
- chained operations;
- realistic stateful testing.

## Realistic business flow

For example:

```text
Login
  ↓
Search product
  ↓
Create cart
  ↓
Add product
  ↓
Checkout
  ↓
Verify order
```

This is the most important example because it demonstrates why Tyfapi exists.

The goal is to show **behavior**, not simply endpoint testing.

---

# 20. AI Validation Experiment

Even though the MVP contains no AI, AI compatibility should be tested manually.

Take an OpenAPI specification and ask several different AI agents to generate Tyfapi YAML.

For example:

```text
OpenAPI
   ↓
Claude
   ↓
Tyfapi YAML
```

Then repeat with other agents such as Codex or Gemini.

No custom integration is required.

The experiment is simply:

> **Can general-purpose AI agents understand the Tyfapi format and produce valid, useful scenarios?**

If the answer is yes, the AI-friendly design hypothesis becomes much stronger.

---

# 21. Product Validation

The MVP is primarily a **market validation experiment**.

The first users should ideally be backend developers.

Give them a real API and ask them to create scenarios such as:

```text
Login
Create resource
Update resource
Delete resource
```

Do not only ask whether they "like the idea."

Observe how they actually use it.

Questions to answer:

- Do they understand the YAML?
- Do they understand Functions and Flows?
- Can they create a scenario without extensive explanation?
- Can they debug failures?
- Is the scenario easier to maintain than their current approach?
- Would they commit the YAML to their repository?
- Would they run it again later?
- Would they put it into CI?
- Would they use it for another scenario?

---

# 22. Success Metrics

The most important MVP metric is **not GitHub stars**.

It is **continued usage**.

## Primary metric — Second-use rate

How many users who try Tyfapi once use it again for a second scenario?

For example:

```text
10 people try Tyfapi
  ↓
8 create first scenario
  ↓
5 create second scenario
  ↓
4 commit scenarios to a repository
  ↓
3 use Tyfapi in CI
```

That would be a strong signal.

A weaker signal would be:

```text
100 people try Tyfapi
  ↓
90 say "cool"
  ↓
2 ever use it again
```

The objective is not curiosity.

The objective is **workflow adoption**.

---

# 23. Strongest Validation Signal

The strongest early signal would be:

> **A developer puts a Tyfapi YAML file into a real repository and keeps using it without being asked.**

Even better:

```text
Scenario
   ↓
Git commit
   ↓
CI
   ↓
Developer changes scenario
   ↓
CI runs again
```

At that point Tyfapi has moved from "interesting tool" to "part of a development workflow."

---

# 24. Suggested MVP Repository

The initial repository can remain very small:

```text
tyfapi/
│
├── src/
│   └── Tyfapi.Cli/
│
├── examples/
│   ├── authentication.yaml
│   ├── crud.yaml
│   └── checkout.yaml
│
├── environments/
│   ├── local.yaml
│   └── example.yaml
│
├── schema/
│   └── tyfapi.schema.json
│
├── README.md
├── LICENSE
└── .gitignore
```

Avoid creating infrastructure for features that have not yet been validated.

---

# 25. Roadmap After MVP

The roadmap should follow evidence from real users.

## Milestone 1 — Core Engine

After MVP validation:

- stabilize the YAML specification;
- improve schema validation;
- improve error reporting;
- improve environment handling;
- improve retries/timeouts;
- improve structured output;
- improve performance;
- expand HTTP capabilities where needed.

## Milestone 2 — CI/CD

Potential capabilities:

- GitHub Action;
- Docker image;
- CI-friendly output;
- `--fail-fast`;
- JUnit-compatible reports;
- running multiple flows;
- secure secret injection;
- pipeline summaries.

Example:

```bash
tyfapi run ./flows --env staging --fail-fast
```

A failed scenario should be able to fail the deployment pipeline.

## Milestone 3 — Agent-Friendly Tooling

Make Tyfapi exceptionally easy for AI coding agents to operate.

Potential capabilities:

- precise JSON schema;
- excellent validation errors;
- structured execution results;
- agent-oriented documentation;
- examples specifically designed for LLM consumption;
- clear CLI semantics;
- deterministic exit codes.

The agent workflow should be:

```text
Generate
   ↓
Validate
   ↓
Run
   ↓
Read result
   ↓
Modify
   ↓
Run again
```

Still:

> **No AI model inside Tyfapi.**

## Milestone 4 — VS Code Extension

Only after the CLI and YAML format are proven.

Potential capabilities:

- automatic CLI installation;
- YAML editing;
- validation feedback;
- flow visualization;
- environment selection;
- run individual steps;
- run complete flows;
- execution results;
- integration with existing AI coding tools.

The extension should remain a UI around the YAML, not a replacement for it.

## Milestone 5 — Local Load Testing

Reuse the same scenarios for traffic simulation.

Example:

```bash
tyfapi load ./flows/checkout.yaml --bots 1000
```

Potential capabilities:

- concurrent scenario workers;
- configurable concurrency;
- duration/iteration limits;
- latency metrics;
- throughput;
- error rates;
- realistic delays.

The goal is:

> **Turn an existing realistic API scenario into traffic with minimal additional configuration.**

## Milestone 6 — Cloud Platform

Only after strong CLI adoption.

Potential capabilities:

- centralized run history;
- dashboards;
- historical latency;
- reliability trends;
- shared scenarios;
- team collaboration;
- CI history;
- notifications;
- access control.

The CLI should remain useful independently.

## Milestone 7 — Distributed Cloud Load Testing

Potential capabilities:

- multi-region traffic generation;
- large-scale Bot execution;
- geographic distribution;
- cloud-managed workers;
- real-time metrics;
- historical performance reports.

This is a natural candidate for a paid service because it consumes infrastructure and provides direct operational value.

---

# 26. Open Source Strategy

Open source can be particularly valuable for Tyfapi because the project introduces a new scenario format.

The core idea is:

> **Users should own their scenarios, not be locked into our cloud.**

The YAML format should be public.

The core runner should be usable locally.

A developer should be able to keep:

```text
checkout.yaml
```

in their own repository and run it without an account.

This provides:

- **Trust** — users can inspect what the runner does.
- **Adoption** — developers can try the tool without signing up.
- **Git-native workflow** — scenarios remain ordinary repository files.
- **Ecosystem growth** — developers can contribute examples, integrations, and improvements.
- **AI compatibility** — public schemas and examples can be consumed by AI coding agents.
- **Reduced lock-in** — scenario files remain useful independently of the cloud service.

---

# 27. Possible Open Source / Commercial Split

A possible long-term model is **open-core**.

## Open Source

Potentially:

- YAML specification;
- JSON schema;
- CLI;
- execution engine;
- local execution;
- environment support;
- variable extraction;
- CI usage;
- local load testing.

## Commercial

Potentially:

- cloud dashboard;
- centralized reporting;
- historical analytics;
- team collaboration;
- hosted runners;
- distributed load testing;
- enterprise authentication;
- RBAC;
- audit logs;
- governance features.

The exact licensing model should be decided later based on adoption and competitive considerations.

---

# 28. Monetization

The project should not depend on charging users for basic local execution.

A potential model:

```text
                 Tyfapi
                    │
          ┌─────────┴─────────┐
          │                   │
     Open Source             Cloud
          │                   │
        CLI              Dashboard
        YAML              Analytics
        Runner            Teams
        CI                History
        Local Load        Hosted Runs
```

Potential free capabilities:

- local execution;
- YAML scenarios;
- Git;
- environment support;
- CI;
- local load testing.

Potential paid capabilities:

- centralized reporting;
- team collaboration;
- hosted execution;
- historical analytics;
- notifications;
- distributed cloud load testing;
- enterprise functionality.

A particularly natural model is:

```text
Local load testing       → Free
Cloud load testing       → Paid
Distributed testing      → Paid
```

The infrastructure cost of the paid feature directly corresponds to its value.

---

# 29. Competitive Positioning

Tyfapi should not attempt to win by becoming "another Postman."

Established API clients are already excellent at:

- exploring APIs;
- manually sending requests;
- inspecting responses;
- organizing requests.

The intended positioning is different:

> **Tyfapi is a Git-native scenario engine for realistic API behavior.**

The core differentiation is the combination of:

```text
Declarative scenarios
        +
Reusable functions
        +
Environment independence
        +
Git
        +
CI/CD
        +
AI-agent compatibility
        +
Future load testing
```

The product should focus on **behavioral scenarios**, not merely endpoint collections.

---

# 30. Non-Goals

To prevent scope creep, the following are not initial goals:

- building a proprietary AI assistant;
- hosting or training an LLM;
- integrating a mandatory AI provider;
- building a Postman clone;
- replacing browser automation;
- replacing specialized load-testing platforms;
- building a complete API design platform;
- building a cloud platform before the CLI is validated;
- building a visual editor before the YAML format is proven;
- supporting every protocol from day one.

Features should be added because real users need them, not because they are technically interesting.

---

# 31. Product Principles

These principles should guide future decisions.

## 1. YAML is the source of truth

Everything else is built around the scenario file.

## 2. AI is optional

Tyfapi works perfectly without AI.

## 3. Deterministic execution

The core engine must remain predictable and reproducible.

## 4. Git first

Scenarios should behave like source code.

## 5. Scenarios over endpoints

The primary abstraction is a user journey through the backend.

## 6. CLI before UI

The core product must work without a graphical interface.

## 7. Automation first

Everything important should be scriptable.

## 8. Machine-readable by design

Tools and AI agents should be able to consume Tyfapi results directly.

## 9. Environment-independent scenarios

Changing the target environment should not require rewriting the scenario.

## 10. Evidence before complexity

Do not build large features until real users demonstrate that they need them.

---

# 32. North Star

Every future product decision should come back to one question:

> **Can a developer define a realistic API scenario once and reliably reuse it everywhere?**

The ideal workflow is:

```text
                 DEFINE ONCE
                      │
                      ▼
                Tyfapi YAML
                      │
          ┌───────────┼───────────┐
          ▼           ▼           ▼
        Local         CI       Staging
                                  │
                                  ▼
                                Prod
```

And with an AI coding agent:

```text
       Developer intent
              │
              ▼
          Any AI Agent
              │
              ▼
         Tyfapi YAML
              │
              ▼
         Tyfapi CLI
              │
              ▼
             API
```

Tyfapi should be the **deterministic layer between developer/agent intent and API execution**.

---

# 33. Final Product Statement

> **Tyfapi is a Git-native, declarative API scenario runner. Define realistic backend workflows once in simple YAML, run them against any environment, execute them locally or in CI, and let any AI coding agent generate or modify the scenarios without making AI a dependency of the product.**

The product is not the AI.

The product is not the dashboard.

The product is not the cloud infrastructure.

The product is:

> **A simple scenario format and a deterministic engine that make realistic API behavior portable, executable, automatable, and reusable everywhere.**

Everything else should grow around that core.
