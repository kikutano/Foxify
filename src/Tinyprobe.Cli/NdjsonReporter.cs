using System.Text.Json;
using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Models;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Protocol;
using Tinyprobe.Core.Reporting;

namespace Tinyprobe.Cli;

/// <summary>
/// <c>--reporter ndjson</c>: one JSON event per line (run_started, step_started, step_completed,
/// run_finished, or invalid), written as soon as it happens so a tool can follow the run live.
/// </summary>
internal sealed class NdjsonReporter(TextWriter output, string version) : IRunReporter
{
    public void WriteLoadErrors(string file, IReadOnlyList<LoadProblem> problems) =>
        Emit(ProtocolJson.Invalid(version, file, problems, eventName: "invalid", indented: false));

    public void WriteHeader(string version, string workflowPath, string? envPath,
        WorkflowDefinition workflow, IReadOnlyDictionary<string, JsonElement> environment) =>
        Emit(ProtocolJson.RunStarted(version, workflow, envPath));

    public async Task<WorkflowReport> RunAsync(
        WorkflowEngine engine, WorkflowDefinition workflow, IReadOnlyDictionary<string, JsonElement> environment)
    {
        engine.StepStarted += progress => Emit(ProtocolJson.StepStarted(progress));
        engine.StepCompleted += step => Emit(ProtocolJson.StepCompleted(step));
        return await engine.ExecuteAsync(workflow, environment);
    }

    public void WriteSummary(WorkflowSummary summary, TimeSpan elapsed) =>
        Emit(ProtocolJson.RunFinished(summary, elapsed));

    private void Emit(string line)
    {
        output.WriteLine(line);
        output.Flush();
    }
}
