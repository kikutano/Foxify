![](logo.jpg)
# ⚡ Foxify

> A lightweight, high-performance, CLI-first API testing and traffic simulation tool built with C# and .NET Native AOT.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET Native AOT](https://img.shields.io/badge/.NET-Native%20AOT-purple.svg)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)

---

## 📌 Overview

**foxify** is a developer-centric alternative to heavy API clients like Postman and code-heavy load testing frameworks like k6. Designed for speed and automation, foxify uses a **declarative YAML/JSON schema** to execute sequential HTTP workflows, extract dynamic state variables in real-time, and run automated assertion checks.

Because it compiles directly to **Native AOT** binaries with zero reflection, foxify boasts sub-10ms startup times, minimal memory consumption, and extreme throughput for local debugging, CI/CD pipelines, and multi-threaded load testing.

---

## ✨ Key Features

- **🚀 Native AOT Performance:** Instant startup and minimal footprint using C# .NET Native AOT and source-generated JSON serialization.
- **📄 Declarative YAML Workflows:** Define reusable request steps (`functions`), dynamic variable extraction (`extract`), and response assertions (`expected` / `asserts`) cleanly in simple YAML or JSON files.
- **🔄 Dynamic In-Flight State:** Automatically parse response bodies using JSONPath (e.g., `$.token`, `$.data[0].id`) and reuse them seamlessly in subsequent endpoints or authorization headers (`Authorization: Bearer ${token}`).
- **🌍 Environment Management:** Decouple environment-specific configuration (`baseUrl`, `apiKey`) into dedicated environment files for local, dev, or production environments.
- **🤖 AI-Friendly Design:** Structured, declarative schemas make it effortless for LLMs to generate, validate, and debug workflows directly from OpenAPI/Swagger specifications.
- **⚙️ CI/CD & Fail-Fast Support:** Native `--fail-fast` execution flags and low-latency execution make it perfect for GitHub Actions and headless test runners.

---

## 🛠️ Installation & Build

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or higher.
- C++ Build Tools (required for Native AOT compilation).

### Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/kikutano/foxify.git
   cd foxify
   ```

2. Restore dependencies and build in Release mode:
   ```bash
   dotnet build -c Release
   ```

3. Publish as a self-contained Native AOT executable:
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained # On Windows
   # dotnet publish -c Release -r linux-x64 --self-contained # On Linux
   # dotnet publish -c Release -r osx-x64 --self-contained # On macOS
   ```

The output executable will be available in `./bin/Release/net10.0/<runtime>/publish/`.

---

## 🚀 Quick Start

### 1. Define Your Environment (`environment.yaml`)

```yaml
baseUrl: "https://reqres.in/"
apiKey: "free_user_3Jia6vmNMqQZQBpYN7Tj2sqeRhp"
```

### 2. Create Your Workflow (`login-flow.yaml`)

```yaml
metadata:
  name: "Authentication & Resource Inspection Flow"
  description: "Authenticates a user, extracts the auth token and ID, and fetches protected resource details."
  api_version: "v1"

functions:
  LoginUser:
    type: HTTP_REQUEST
    method: POST
    baseurl: "${baseUrl}"
    endpoint: "api/login"
    headers:
      Content-Type: application/json
      x-api-key: "${apiKey}"
    body: |
      {
        "email": "eve.holt@reqres.in",
        "password": "cityslicka"
      }
    expected:
      status_code: 200
    extract:
      authToken: $.token

  GetUserDetails:
    type: HTTP_REQUEST
    method: GET
    baseurl: "${baseUrl}"
    endpoint: "api/users/2"
    headers:
      Authorization: "Bearer ${authToken}"
      x-api-key: "${apiKey}"
    expected:
      status_code: 200
    extract:
      userEmail: $.data.email
      firstName: $.data.first_name
    asserts:
      userEmail: "== 'janet.weaver@reqres.in'"

workflow:
  - type: function
    function_name: LoginUser

  - name: AuthCoolOff
    type: DELAY
    duration_seconds: 0.5

  - type: function
    function_name: GetUserDetails
    depends_on: [LoginUser]

settings:
  timeout: 10
  max_retries: 3
```

### 3. Run the Workflow via CLI

Validate workflow syntax without executing network calls:

Run the workflow passing the environment file:
```bash
foxify <path-to-flow.yaml> [path-to-environment.yaml]
```

---

## 💻 CLI Usage & Commands

```text
Usage:
  foxify [command] [options]

Commands:
  run <workflow-path>       Executes the specified workflow YAML file.
  validate <workflow-path>  Validates the workflow YAML schema without running execution.

Options for 'run':
  -e, --env <path>          Path to environment file (.yaml or .json) with initial variables.
  --fail-fast               Immediately aborts workflow execution on the first failed assertion or HTTP error.
  -v, --verbose             Displays detailed request and response headers and payload logs.
  --timeout <seconds>       Overrides global default timeout per single HTTP request.
```

---

## 🗺️ Project Roadmap

- [x] **Fase 1: Core Engine & CLI**
  - [x] Schema definition (YAML/JSON)
  - [x] Native AOT compilation & System.Text.Json Source Generation
  - [x] Variable storage engine & placeholder interpolation (`${var}`)
  - [x] HTTP request step execution & delay steps
  - [x] Basic JSONPath response extraction and status assertions
- [ ] **Fase 2: Developer Experience & VS Code Integration**
  - [ ] Native VS Code extension wrapper
  - [ ] Visual UI / Split-screen editor (Monaco + Webview)
  - [ ] Local AI Agent for workflow creation from OpenAPI specifications
- [ ] **Fase 3: Load Testing & AI Diagnostics**
  - [ ] Multi-threaded bot runner (Simultaneous Traffic Generation)
  - [ ] Official GitHub Action & Distroless Docker Image
  - [ ] Automated LLM-based failure diagnostic logs
- [ ] **Fase 4: Cloud & Enterprise**
  - [ ] Distributed load testing agents
  - [ ] Cloud reporting dashboard and execution history

---

## 📄 License

Distributed under the MIT License. See [`LICENSE`](LICENSE) for more information.