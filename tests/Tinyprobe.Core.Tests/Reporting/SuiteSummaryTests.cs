using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Models;
using Tinyprobe.Core.Reporting;

namespace Tinyprobe.Core.Tests.Reporting;

public class SuiteSummaryTests
{
    private static readonly SourceLocation Here = new("test.yaml", 1, 1);

    private static WorkflowDefinition WorkflowWithSteps(int callSteps) => new()
    {
        Steps = Enumerable.Range(0, callSteps)
            .Select(i => (StepDefinition)new CallStep { Name = $"S{i}", Call = "ping", Location = Here })
            .ToList()
    };

    private static SuiteEntry Entry(string file, int callSteps, params StepReport[] steps)
    {
        var report = new WorkflowReport();
        report.StepReports.AddRange(steps);
        return SuiteEntry.Create(file, WorkflowWithSteps(callSteps), report, TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public void Totals_AddUpTheWorkflows()
    {
        var suite = new SuiteSummary([
            Entry("auth.yaml", 2, new StepReport(), new StepReport()),
            Entry("users.yaml", 3, new StepReport(), new StepReport { StepName = "Create", IsSuccess = false, FailureReason = "Expected status 201, got 500" })
        ]);

        Assert.False(suite.IsSuccess);
        Assert.Equal((2, 1, 1), (suite.TotalWorkflows, suite.PassedWorkflows, suite.FailedWorkflows));
        Assert.Equal((5, 3, 1, 1), (suite.TotalSteps, suite.PassedSteps, suite.FailedSteps, suite.NotRunSteps));
    }

    [Fact]
    public void FirstFailure_NamesTheStepAndTheReason()
    {
        var entry = Entry("users.yaml", 3,
            new StepReport { StepName = "List" },
            new StepReport { StepName = "Create", IsSuccess = false, FailureReason = "Expected status 201, got 500" });

        Assert.Equal("Create: Expected status 201, got 500", entry.FirstFailure);
        Assert.Null(Entry("ok.yaml", 1, new StepReport()).FirstFailure);
    }

    [Fact]
    public void FirstFailure_OfAFailedCheck_ShowsTheActualValue()
    {
        var entry = Entry("users.yaml", 1, new StepReport
        {
            StepName = "Read",
            IsSuccess = false,
            Assertions =
            {
                new() { Subject = "$.id", Condition = "== 1", ActualValue = "1", Passed = true },
                new() { Subject = "$.name", Condition = "== \"Ada\"", ActualValue = "\"Bob\"", Passed = false }
            }
        });

        Assert.Equal("Read: $.name == \"Ada\", actual \"Bob\"", entry.FirstFailure);
    }
}
