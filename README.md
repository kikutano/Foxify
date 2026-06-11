# Project Roadmap & Architecture Strategy

This document outlines the development milestones and architectural choices for our next-generation, Git-friendly, AI-first API testing and traffic simulation tool.

---

## 🏗️ Architectural Overview

To ensure maximum portability, performance, and flexibility, the project decouples the execution engine from the user interface using a **CLI-First Architecture**.

* **The Core Engine (CLI):** A single, dependency-free binary built using **.NET (C#) with Native AOT (Ahead-Of-Time)** compilation. This guarantees near-instant startup times (< 10ms), zero external runtime dependencies, and a minimal footprint (approx. 10-15MB), making it exceptionally performant and lightweight for CI/CD pipelines. It is responsible for parsing flow files, managing state/environments, executing HTTP requests, and handling massive parallel traffic (Bots).
* **The Interface (UI):** A unified web-based frontend components layer (React/Svelte + Tailwind CSS) featuring Monaco Editor for a seamless code/visual split.
* **Distribution Channels:**
    * **VS Code Extension:** Runs the UI via a Webview panel and communicates with the local CLI binary using Node.js child processes.
    * **Web App:** Offers a cloud-based visual representation for reporting, collaboration, and remote execution, bypassing browser CORS and sandbox limitations.

---

## 📍 Milestone 1: The Core Engine (CLI) & Syntax Definition
*The goal of this phase is to build a rock-solid, autonomous execution engine, design an AI-optimized test syntax, and ensure perfect compatibility with Native AOT.*

### Key Objectives
* **Syntax Specification & AI Validation:** Design a clean, flat test flow format (YAML or JSON) optimized for human readability and LLM generation (minimal nesting, clear semantics). As a preliminary step, validate the schema by prompting standard LLMs (GPT/Claude) to convert OpenAPI/Swagger specs into our custom flow format to ensure reliable generation without errors.
* **Core CLI Development (.NET Native AOT):** Build the command-line interface using C# Native AOT. Implement strict AOT-compliant practices:
    * **Zero Reflection:** Enforce the use of **C# Source Generators** (e.g., `System.Text.Json` with compiled `JsonSourceGenerationOptions`) for high-performance, reflection-free configuration and YAML/JSON parsing.
    * **High-Throughput I/O:** Leverage standard `.NET` `HttpClient` backed by an optimized `SocketsHttpHandler` to manage concurrent requests efficiently.
* **CLI Commands:** Implement core base commands:
    * `mytool validate ./test.yaml` (syntax and schema validation).
    * `mytool run ./tests --env dev.json` (sequential execution and variable extraction).
* **The Killer Feature (Flows & Delays):** Implement dynamic variable passing between sequential requests (e.g., extracting a token from response A and injecting it into headers for response B) and realistic production-like delays.
* **Traffic Simulation Foundations (Bots):** Architect the execution engine layer to natively support high-concurrency cloning of flow steps. While massive load testing is deferred to later milestones, the core loop must be non-blocking and ready to spin up multiple parallel workers ("Bots") to execute identical flows continuously.

---

## 📍 Milestone 2: Developer Experience (VS Code Extension)
*The goal is to integrate the tool directly into the developer's daily workflow, removing friction and enabling AI-assisted test generation.*

### Key Objectives
* **Extension Wrapper:** Build the VS Code extension that automatically packages and installs the correct OS-specific C# native binary under the hood.
* **Bidirectional Webview UI:** Develop the visual panel inside VS Code. Ensure real-time, two-way synchronization: modifying the visual UI edits the underlying text file, and typing in the code editor instantly updates the visual components.
* **In-Editor AI Agent:** Integrate a dedicated AI chat/prompter within the extension interface.
    * *User prompt:* `"Generate a flow that tests the e-commerce checkout loop."`
    * *Action:* The AI generates the flow file in the background, and the UI dynamically renders the newly created endpoints on screen.

---

## 📍 Milestone 3: Automation, Load Testing & Pipeline Integration (CI/CD)
*Targeting team collaboration, performance testing, and enterprise reliability by enforcing API and load validation during the deployment cycle.*

### Key Objectives
* **Native CI/CD Packages:** Deliver an official GitHub Action and an ultra-lightweight Docker image (using Alpine or Distroless bases) containing the standalone native AOT CLI engine.
* **Distributed & Local Load Testing:** Unlock the concurrent engine layer. Allow developers to execute load and performance testing directly from the CLI/CI environment by expanding a single YAML flow file into thousands of concurrent local "Bots".
* **Pipeline Fail-Fast:** Allow teams to integrate commands like `mytool run ./flows --fail-fast` into their CI scripts. If an end-to-end flow fails or a bot simulation surfaces a bottleneck, the deployment pipeline safely halts.
* **AI Error Diagnostics:** Implement an advanced AI log analyzer. When a pipeline test fails (e.g., a `500 Internal Server Error`), the CLI can pipe the request payload, headers, and server response to the AI, returning an instant, human-readable bug explanation and a suggested fix back to the developer.

---

## 📍 Milestone 4: Cloud Platform (SaaS & Monetization)
*Transforming the tool into a collaborative web platform that unifies engineering teams and unlocks scalable business value.*

### Key Objectives
* **Cloud Dashboard:** Launch the SaaS web interface where developers and product managers can log in, view metrics, and manage shared API specs.
* **Centralized Reporting:** Update the CLI to support cloud sync (e.g., `--report-to-cloud`). Tests run locally or in GitHub Actions automatically stream results to the web platform.
* **Management & Analytics UI:** Display historical API reliability charts, response latency trends, and performance reports generated during bot simulation runs.
* **Distributed Cloud Load Testing (Monetization):** Introduce premium distributed testing capabilities. Allow users to trigger massive traffic simulations (tens of thousands of Bots) running not just locally, but distributed across multiple global cloud regions managed by our platform.

---

## 🧠 The AI-Driven Development Lifecycle
[INPUT] API Specifications (OpenAPI / Swagger)
│
▼
[AI GENERATOR] In-Editor AI constructs the Flow File (YAML/JSON)
│
▼
[VISUAL UI / TEXT] Developer tweaks the flow using either the UI or raw code
│
▼
[EXECUTION] Ultra-performant local C# Native AOT CLI executes the Flow or parallel Bots
│
▼
[CI/CD / WEB REPORT] Test runs in pipeline (lightweight container), streaming results to Cloud Dashboard
│
▼
[AI DIAGNOSTIC] On failure, AI isolates the bug, explains the root cause, and auto-corrects the flow
