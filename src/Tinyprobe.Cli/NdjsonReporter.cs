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
/// run_finished, or invalid; suite_started and suite_finished around several workflows), written as soon as it happens so a tool can follow the run live.
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
        // The iterations of a for_each step are sent together, in one step_completed, after the last one.
        var iterations = new List<StepReport>();
        engine.StepCompleted += step =>
        {
            if (step.Iterations is not { } count || count == 0)
            {
                Emit(ProtocolJson.StepCompleted(step));
                return;
            }
            iterations.Add(step);
            if (step.Iteration == count)
            {
                Emit(ProtocolJson.StepCompleted(iterations));
                iterations.Clear();
            }
        };
        return await engine.ExecuteAsync(workflow, environment);
    }

    public void WriteSummary(WorkflowSummary summary, TimeSpan elapsed) =>
        Emit(ProtocolJson.RunFinished(summary, elapsed));

    public void WriteSuiteLoadErrors(string target, IReadOnlyList<LoadProblem> problems) =>
        WriteLoadErrors(target, problems);

    public void WriteSuiteStart(string version, IReadOnlyList<string> files) =>
        Emit(ProtocolJson.SuiteStarted(version, files));

    public void WriteSuiteSummary(SuiteSummary suite, TimeSpan elapsed) =>
        Emit(ProtocolJson.SuiteFinished(suite, elapsed));

    private void Emit(string line)
    {
        output.WriteLine(line);
        output.Flush();
    }
}
