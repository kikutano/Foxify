using System.Text.Json;
using Foxify.Core.Definitions;
using Foxify.Core.Execution;
using Foxify.Core.Models;
using Foxify.Core.Parsing;
using Foxify.Core.Reporting;

namespace Foxify.Cli;

/// <summary>Shows a run of <c>foxify run</c> or <c>foxify call</c>: for people (console) or tools (ndjson).</summary>
internal interface IRunReporter
{
    void WriteLoadErrors(string file, IReadOnlyList<LoadProblem> problems);

    void WriteHeader(string version, string workflowPath, string? envPath,
        WorkflowDefinition workflow, IReadOnlyDictionary<string, JsonElement> environment);

    Task<WorkflowReport> RunAsync(
        WorkflowEngine engine, WorkflowDefinition workflow, IReadOnlyDictionary<string, JsonElement> environment);

    void WriteSummary(WorkflowSummary summary, TimeSpan elapsed);
}
