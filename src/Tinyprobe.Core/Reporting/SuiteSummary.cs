using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Models;

namespace Tinyprobe.Core.Reporting;

/// <summary>One workflow of a suite (<c>tinyprobe run</c> on a folder or several files).</summary>
/// <param name="File">The path as found, for people; protocol events use the full path.</param>
/// <param name="FirstFailure">The first failing step and why, or null when the workflow passed.</param>
public sealed record SuiteEntry(string File, WorkflowSummary Summary, TimeSpan Elapsed, string? FirstFailure)
{
    public static SuiteEntry Create(string file, WorkflowDefinition workflow, WorkflowReport report, TimeSpan elapsed) =>
        new(file, WorkflowSummary.Create(workflow, report), elapsed, Describe(report.StepReports.FirstOrDefault(s => !s.IsSuccess)));

    private static string? Describe(StepReport? step)
    {
        if (step is null)
            return null;

        if (step.FailureReason is not null)
            return $"{step.StepName}: {step.FailureReason}";

        var check = step.Assertions.FirstOrDefault(a => !a.Passed);
        return check is null
            ? step.StepName
            : $"{step.StepName}: {check.Subject} {check.Condition}, actual {check.ActualValue}";
    }
}

/// <summary>The totals of a suite: every workflow ran, whether or not an earlier one failed.</summary>
public sealed class SuiteSummary(IReadOnlyList<SuiteEntry> entries)
{
    public IReadOnlyList<SuiteEntry> Entries { get; } = entries;

    public int TotalWorkflows => Entries.Count;
    public int PassedWorkflows => Entries.Count(e => e.Summary.IsSuccess);
    public int FailedWorkflows => TotalWorkflows - PassedWorkflows;

    public int TotalSteps => Entries.Sum(e => e.Summary.TotalSteps);
    public int PassedSteps => Entries.Sum(e => e.Summary.PassedSteps);
    public int FailedSteps => Entries.Sum(e => e.Summary.FailedSteps);
    public int NotRunSteps => Entries.Sum(e => e.Summary.NotRunSteps);

    public bool IsSuccess => FailedWorkflows == 0;
}
