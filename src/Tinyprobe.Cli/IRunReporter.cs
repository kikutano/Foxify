using System.Text.Json;
using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Models;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Reporting;

namespace Tinyprobe.Cli;

/// <summary>Shows a run of <c>tinyprobe run</c> or <c>tinyprobe call</c>: for people (console) or tools (ndjson).</summary>
internal interface IRunReporter
{
    void WriteLoadErrors(string file, IReadOnlyList<LoadProblem> problems);

    void WriteHeader(string version, string workflowPath, string? envPath,
        WorkflowDefinition workflow, IReadOnlyDictionary<string, JsonElement> environment);

    Task<WorkflowReport> RunAsync(
        WorkflowEngine engine, WorkflowDefinition workflow, IReadOnlyDictionary<string, JsonElement> environment);

    void WriteSummary(WorkflowSummary summary, TimeSpan elapsed);
}
