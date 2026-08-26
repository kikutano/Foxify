# Tyfapi

> **Define API scenarios once. Run them anywhere. Let any AI agent generate them.**

Tyfapi is a **Git-native, declarative API scenario runner** designed to make realistic backend verification easy to define, reproduce, automate, and execute across environments.

Tyfapi is **AI-friendly, not AI-powered**.

It does not contain an AI model, does not require an AI provider, and does not lock users into a specific LLM. Users can use any LLM or coding agent they prefer — or no AI at all — to create and modify Tyfapi YAML files.

The core product is a deterministic execution engine and a simple, machine-readable scenario format.

---

## 1. The Problem

API testing is often fragmented across:

- manually written test code;
- Postman or similar collections;
- shell scripts;
- integration-test frameworks;
- custom CI scripts;
- load-testing tools;
- environment-specific configurations.

Individual endpoints are relatively easy to test.

The harder problem is verifying **realistic user journeys through a backend**.

For example:

```text
Login
  ↓
Extract authentication token
  ↓
Get profile
  ↓
Create cart
  ↓
Add product
  ↓
Checkout
  ↓
Verify order
```

These scenarios contain state, dependencies, extracted values, ordering, delays, and environment-specific configuration.

Tyfapi aims to represent those scenarios as **version-controlled declarative files** that can be executed consistently anywhere.

---

# 2. The Core Idea

Tyfapi is built around three concepts:

### Function

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

### Flow

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

### Environment

The same scenario can run against different targets without changing the scenario itself.

```bash
tyfapi run checkout.yaml --env local
tyfapi run checkout.yaml --env dev
tyfapi run checkout.yaml --env staging
tyfapi run checkout.yaml --env prod
```

The same Git-tracked scenario can therefore be reused for:

- local development;
- integration testing;
- staging verification;
- CI/CD;
- production smoke tests;
- future load testing.

---

# 3. What Tyfapi Is

Tyfapi is:

- **Declarative** — scenarios are described as YAML rather than imperative test code.
- **Git-native** — scenarios are ordinary text files that can be versioned, reviewed, diffed, and shared.
- **Deterministic** — the execution engine itself does not depend on an AI model.
- **AI-friendly** — the format is intentionally simple and structured so LLMs and coding agents can generate and modify it reliably.
- **Environment-independent** — scenarios should not need to change when the target environment changes.
- **CLI-first** — the execution engine is designed for developers and CI/CD pipelines first.
- **Composable** — reusable Functions can be combined into realistic Flows.
- **Machine-readable** — validation and execution results should be available in structured formats suitable for automation and AI agents.
- **Portable** — the core runner should be distributed as a lightweight standalone executable.

---

# 4. What Tyfapi Is NOT

Tyfapi is deliberately **not**:

- an AI assistant;
- an LLM provider;
- a ChatGPT replacement;
- a hosted-only testing platform;
- a browser automation framework;
- a traditional GUI-first API client;
- a replacement for every existing load-testing tool.

Tyfapi should not require users to send their source code, API definitions, requests, or credentials to a third-party AI service.

If a user wants AI assistance, they choose the provider or agent themselves.

For example:

```text
Claude Code
Codex
Gemini CLI
Copilot
Local LLM
Any future agent
        ↓
   Tyfapi YAML
        ↓
   Tyfapi CLI
        ↓
      API
```

---

# 5. The AI-Friendly Philosophy

The key principle is:

> **AI should be a consumer and producer of Tyfapi files, not a dependency of Tyfapi itself.**

An AI coding agent should be able to:

1. read an OpenAPI specification;
2. inspect an existing Tyfapi repository;
3. understand the available Functions and environments;
4. generate a new scenario;
5. run Tyfapi;
6. consume structured validation or execution results;
7. modify the YAML when something fails;
8. run the scenario again.

Conceptually:

```text
             OpenAPI / Source Code
                      │
                      ▼
                AI / Agent
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
            Machine-readable result
                      │
                      ▼
                  AI / Agent
```

Tyfapi does not need to know which AI model is involved.

This keeps the project independent from rapidly changing AI providers and allows the ecosystem to evolve without requiring changes to the core product.

---

# 6. Why YAML?

YAML is intended to be:

- human-readable;
- Git-friendly;
- easy to review;
- easy to diff;
- expressive enough for API scenarios;
- easy for LLMs to generate;
- easy for tools to parse;
- independent from a programming language.

The syntax should intentionally avoid unnecessary complexity.

The goal is not to create a programming language.

The goal is to create a **small declarative language for describing API behavior**.

A good Tyfapi file should be understandable by:

- a backend developer;
- a CI pipeline;
- an AI coding agent;
- the Tyfapi CLI.

---

# 7. Example

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

# 8. Variable Extraction

Tyfapi supports extracting values from API responses and reusing them later in a Flow.

Example:

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

Variables can then be used in subsequent requests:

```yaml
GetProfile:
  type: HTTP_REQUEST
  method: GET
  endpoint: /users/${user_id}
  headers:
    Authorization: "Bearer ${token}"
```

Initial JSONPath support includes:

```text
$.token
$.user.id
$.user.username
$.items[0].name
```

The syntax may evolve, but the principle should remain simple:

> **Capture values from one step and make them available to later steps.**

---

# 9. Dependencies and Flow Semantics

Flows should explicitly describe dependencies between steps.

Example:

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

Dependencies communicate both to humans and AI agents that:

```text
Login
  ↓
CreateCart
  ↓
AddProduct
```

The engine is responsible for enforcing the execution semantics.

The initial implementation should prioritize predictable sequential execution.

Future versions may support more advanced dependency graphs and controlled parallelism.

---

# 10. Environment Independence

A major design goal is:

> **The scenario should describe behavior, not infrastructure.**

For example:

```yaml
baseurl: ${baseUrl}
```

The environment provides the actual value.

Conceptually:

```text
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

This makes the same Flow usable for:

```text
local → CI → dev → staging → production
```

without duplicating test definitions.

Secrets should not be committed into scenario files.

---

# 11. Machine-Readable Execution

Because Tyfapi is intended to work well with AI agents and automation, CLI output should support both:

### Human-readable output

```text
✓ LoginUser              182ms
✓ GetProfile              43ms

2/2 steps passed
Total: 225ms
```

### Machine-readable output

For example:

```bash
tyfapi run checkout.yaml --format json
```

could produce structured data such as:

```json
{
  "status": "failed",
  "flow": "checkout",
  "step": "Checkout",
  "request": {
    "method": "POST",
    "endpoint": "/checkout"
  },
  "response": {
    "status": 500
  },
  "duration_ms": 241
}
```

The exact schema is to be defined.

The important principle is that **agents and automation should be able to understand failures without parsing human-oriented terminal text**.

This is a first-class product requirement, not an afterthought.

---

# 12. Architecture

Tyfapi follows a **CLI-first architecture**.

```text
                 ┌──────────────────────┐
                 │   Tyfapi YAML Files  │
                 └──────────┬───────────┘
                            │
                            ▼
                 ┌──────────────────────┐
                 │    Tyfapi CLI/Core   │
                 │                      │
                 │  Parser              │
                 │  Validator           │
                 │  Variable Engine     │
                 │  Flow Executor       │
                 │  HTTP Engine         │
                 │  Result Reporter     │
                 └──────────┬───────────┘
                            │
                ┌───────────┼───────────┐
                ▼           ▼           ▼
              Local         CI       Remote API
```

The core engine must remain usable without a web application.

The CLI is the primary interface.

---

# 13. Core Technology

The initial implementation uses:

- **C# / .NET**
- **Native AOT**
- `HttpClient` / `SocketsHttpHandler`
- source-generated serialization where appropriate
- a lightweight YAML parser compatible with the project's AOT requirements

The objectives are:

- fast startup;
- small footprint;
- low operational overhead;
- easy distribution;
- no external runtime dependency;
- suitability for CI/CD;
- efficient concurrent HTTP execution.

Native AOT is an implementation choice, not the primary product value proposition.

The user value is:

> **Run the same API scenario reliably anywhere.**

---

# 14. CLI

The initial CLI should provide a small and predictable command surface.

### Validate

```bash
tyfapi validate ./flows/checkout.yaml
```

### Run

```bash
tyfapi run ./flows/checkout.yaml --env dev
```

### Machine-readable execution

```bash
tyfapi run ./flows/checkout.yaml --env dev --format json
```

Future commands may include:

```bash
tyfapi run ./flows --env staging
tyfapi load ./flows/checkout.yaml --bots 1000
```

The CLI should remain useful even if the user never uses the web application or VS Code extension.

---

# 15. Project Principles

These principles are the source of truth for product decisions.

## Principle 1 — The YAML is the source of truth

The scenario file is the canonical representation.

UI, cloud services, and other tooling must operate around the YAML rather than replacing it with a proprietary internal representation.

---

## Principle 2 — AI is optional

Tyfapi must work perfectly without AI.

AI integration means:

```text
Any AI → Tyfapi YAML → Tyfapi
```

not:

```text
Tyfapi → mandatory AI provider
```

---

## Principle 3 — Deterministic execution

Given the same environment, inputs, and scenario, execution should be predictable.

The core engine should not depend on probabilistic behavior.

---

## Principle 4 — Git first

Tyfapi files should behave like source code:

- commit them;
- review them;
- diff them;
- branch them;
- merge them;
- run them in CI.

---

## Principle 5 — Scenarios over endpoints

The core abstraction is not:

> "Test this endpoint."

It is:

> "Verify this behavior through the system."

Endpoint-level testing is a building block.

Flows are the product abstraction.

---

## Principle 6 — CLI before UI

The core engine must provide real value before any visual interface exists.

A web dashboard or VS Code extension should improve the experience, not be required for basic functionality.

---

## Principle 7 — Automation first

Everything important should be automatable:

- validation;
- execution;
- CI;
- reporting;
- future load testing;
- AI-agent interaction.

---

# 16. What Tyfapi Could Eventually Become

The long-term vision is:

```text
                  Tyfapi Scenario
                        │
          ┌─────────────┼─────────────┐
          │             │             │
        Local           CI         Production
          │             │             │
          └─────────────┼─────────────┘
                        │
                  Same definition
                        │
          ┌─────────────┼─────────────┐
          │             │             │
       Regression     Smoke        Load
         Tests        Tests       Testing
```

A single scenario definition can become the common language for different forms of backend verification.

---

# 17. Roadmap

The roadmap is intentionally ordered around **validation of the core product before building the platform around it**.

---

## Milestone 0 — Product Validation

**Goal:** Prove that developers actually want Git-native API scenarios.

Before investing heavily in the complete platform, validate the concept with a small working prototype.

### Objectives

- Build a minimal CLI.
- Support a minimal YAML schema.
- Support HTTP requests.
- Support sequential workflows.
- Support variable extraction.
- Support environment variables.
- Support structured output.
- Create 3–5 realistic example scenarios.
- Test the workflow with real backend developers.

### Success signal

The most important metric is not GitHub stars.

It is:

> **Do developers put Tyfapi files into real repositories and execute them again after the first trial?**

---

# Milestone 1 — Core Engine

**Goal:** Build a reliable, standalone API scenario execution engine.

### Objectives

- Define the stable YAML specification.
- Define schema validation rules.
- Implement HTTP requests.
- Implement environment resolution.
- Implement variable substitution.
- Implement JSON response extraction.
- Implement workflow execution.
- Implement dependencies.
- Implement delays.
- Implement timeouts.
- Implement retries.
- Implement clear failure reporting.
- Implement machine-readable output.
- Ensure Native AOT compatibility.
- Support concurrent execution primitives in the architecture without prematurely building distributed load testing.

### Initial supported HTTP methods

```text
GET
POST
PUT
PATCH
DELETE
```

Additional methods may be added later.

### Success criteria

A developer should be able to:

```bash
tyfapi validate ./flows/checkout.yaml
tyfapi run ./flows/checkout.yaml --env local
```

and obtain reliable, understandable results.

---

# Milestone 2 — Git & CI/CD

**Goal:** Make Tyfapi useful as a serious development and deployment tool.

### Objectives

- GitHub Action.
- Lightweight Docker image.
- CI-friendly exit codes.
- `--fail-fast`.
- JSON output.
- JUnit-compatible reporting if useful.
- Run multiple flows.
- Environment configuration suitable for CI.
- Secure secret injection.
- Clear pipeline summaries.

Example:

```bash
tyfapi run ./flows --env staging --fail-fast
```

A failed scenario should be able to fail the deployment pipeline.

---

# Milestone 3 — Agent-Friendly Tooling

**Goal:** Make Tyfapi exceptionally easy for AI coding agents to use without embedding AI into the product.

### Objectives

- Publish a precise machine-readable schema.
- Provide excellent validation errors.
- Provide structured execution results.
- Document the format for LLMs.
- Provide examples designed for agent consumption.
- Provide instructions/prompts users can optionally give to their preferred agents.
- Ensure agents can perform:

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

### Important constraint

No AI model is added to the Tyfapi core.

The project remains model/provider agnostic.

---

# Milestone 4 — VS Code Extension

**Goal:** Reduce friction while keeping YAML and Git as the source of truth.

### Objectives

- Cross-platform extension.
- Automatic CLI installation.
- YAML editing.
- Validation feedback.
- Flow visualization.
- Run individual steps or complete flows.
- Environment selection.
- Human-friendly execution results.
- Optional integration with the user's existing AI coding tools.

The visual interface should be a representation of the YAML, not a replacement for it.

---

# Milestone 5 — Local Load Testing

**Goal:** Reuse the same scenario definitions for traffic simulation.

Example:

```bash
tyfapi load ./flows/checkout.yaml --bots 1000
```

### Objectives

- Clone a Flow into concurrent workers ("Bots").
- Support configurable concurrency.
- Support duration and iteration limits.
- Collect latency and throughput metrics.
- Collect error rates.
- Support realistic delays.
- Reuse existing Functions and Flows.
- Keep load testing based on the same scenario definition.

The goal is not initially to replace specialized load-testing platforms.

The goal is:

> **Turn an existing realistic API scenario into traffic with minimal additional configuration.**

---

# Milestone 6 — Cloud Platform

**Goal:** Add collaboration and centralized reporting without making the cloud platform mandatory.

Potential capabilities:

- team accounts;
- centralized run history;
- dashboards;
- historical latency;
- reliability trends;
- shared API specifications;
- scenario management;
- CI run history;
- notifications;
- access control.

The CLI should remain useful independently.

---

# Milestone 7 — Distributed Cloud Load Testing

**Goal:** Provide managed distributed traffic generation.

Potential capabilities:

- multi-region load generation;
- large-scale Bot execution;
- configurable geographic distribution;
- cloud-managed workers;
- real-time metrics;
- historical reports.

This is a potential premium capability because it consumes infrastructure and provides clear direct value.

---

# 18. Monetization Strategy

The core philosophy should favor a **free/open-source or generous free CLI**.

The objective is to maximize adoption of the scenario format.

Potential model:

### Free / Open Source

- CLI;
- YAML format;
- local execution;
- environment support;
- variable extraction;
- Git integration;
- CI execution;
- local load testing.

### Cloud / Pro

- centralized reporting;
- historical runs;
- team collaboration;
- advanced dashboards;
- notifications;
- hosted CI results;
- advanced analytics.

### Enterprise

Potential capabilities:

- SSO;
- RBAC;
- audit logs;
- private runners;
- enterprise integrations;
- self-hosted deployment;
- governance features.

### Cloud Load Testing

Potentially usage-based:

```text
Local load testing     → Free
Cloud load testing     → Paid
Distributed testing    → Paid
```

This creates a natural relationship between cost and revenue.

The project does not need to monetize the basic CLI to be commercially viable.

---

# 19. Competitive Positioning

Tyfapi should not attempt to win by being "another Postman."

Existing tools are already strong at API exploration and endpoint testing.

The intended positioning is closer to:

> **A Git-native scenario engine for realistic API behavior.**

The key distinction is the combination of:

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

The competitive advantage should come from the workflow and the format, not from simply having more HTTP features than established API clients.

---

# 20. Non-Goals

To avoid scope creep, the following are not initial goals:

- building a proprietary AI assistant;
- training or hosting an LLM;
- replacing browser automation frameworks;
- replacing every load-testing platform;
- building a full API design platform;
- building a Postman clone;
- building a visual editor before the CLI is proven;
- building a SaaS platform before the core scenario format is validated.

---

# 21. MVP Definition

The first genuinely useful version of Tyfapi should be surprisingly small.

It needs to prove this loop:

```text
API specification / developer intent
              ↓
       YAML scenario
              ↓
       tyfapi validate
              ↓
         tyfapi run
              ↓
          API system
              ↓
     structured result
```

The MVP should support:

- HTTP requests;
- reusable Functions;
- sequential Flows;
- variables;
- response extraction;
- environment configuration;
- dependencies;
- delays;
- retries;
- validation;
- human-readable output;
- machine-readable output;
- non-zero exit codes on failure.

Everything else comes later.

---

# 22. The North Star

The project should always come back to one question:

> **Can a developer define a realistic API scenario once and reliably reuse it everywhere?**

The ideal workflow is:

```text
              DEFINE ONCE
                   │
                   ▼
             Tyfapi YAML
                   │
        ┌──────────┼──────────┐
        ▼          ▼          ▼
      Local        CI       Staging
                              │
                              ▼
                            Prod
```

And when an AI coding agent is available:

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

# 23. Final Product Statement

> **Tyfapi is a Git-native, declarative API scenario runner. Define realistic backend workflows once in simple YAML, run them against any environment, execute them locally or in CI, and let any AI coding agent generate or modify the scenarios without making AI a dependency of the product.**

The product is not the AI.

The product is not the dashboard.

The product is not the load-testing infrastructure.

**The product is the scenario format and the engine that makes those scenarios portable, executable, automatable, and useful everywhere.**

Everything else should grow around that core.
