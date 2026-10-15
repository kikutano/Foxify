using System.Net;
using Foxify.Core.Definitions;
using Foxify.Core.Models;
using Foxify.Core.Reporting;

namespace Foxify.Core.Tests.Reporting;

public class WorkflowSummaryTests
{
    private static readonly SourceLocation Here = new("test.yaml", 1, 1);

    private static WorkflowDefinition WorkflowWithSteps(int callSteps, int delays = 0)
    {
        var steps = new List<StepDefinition>();
        for (int i = 0; i < callSteps; i++)
            steps.Add(new CallStep { Name = $"F{i}", Call = $"F{i}", Location = Here });
        for (int i = 0; i < delays; i++)
            steps.Add(new SleepStep { Duration = TimeSpan.FromSeconds(1), Location = Here });
        return new WorkflowDefinition { Steps = steps };
    }

    private static StepReport Step(int ms, bool success = true, HttpStatusCode? status = HttpStatusCode.OK) => new()
    {
        IsSuccess = success,
        StatusCode = status,
        Duration = TimeSpan.FromMilliseconds(ms)
    };

    [Fact]
    public void Counts_StepsChecksAndBytes()
    {
        var report = new WorkflowReport
        {
            StepReports =
            {
                new StepReport
                {
                    StatusCode = HttpStatusCode.OK,
                    ExpectedStatusCode = HttpStatusCode.OK,
                    BytesSent = 10,
                    BytesReceived = 100,
                    Assertions = { new() { Passed = true }, new() { Passed = true } }
                },
                new StepReport
                {
                    IsSuccess = false,
                    StatusCode = HttpStatusCode.OK,
                    ExpectedStatusCode = HttpStatusCode.OK,
                    BytesReceived = 50,
                    Assertions = { new() { Passed = false } }
                }
            }
        };

        var summary = WorkflowSummary.Create(WorkflowWithSteps(callSteps: 4, delays: 2), report);

        Assert.Equal(4, summary.TotalSteps);
        Assert.Equal(1, summary.PassedSteps);
        Assert.Equal(1, summary.FailedSteps);
        Assert.Equal(2, summary.NotRunSteps);
        Assert.Equal(4, summary.ChecksPassed);   // 2 status + 2 asserts
        Assert.Equal(1, summary.ChecksFailed);
        Assert.Equal(2, summary.HttpRequests);
        Assert.Equal(10, summary.BytesSent);
        Assert.Equal(150, summary.BytesReceived);
        Assert.False(summary.IsSuccess);
    }

    [Fact]
    public void WrongStatus_CountsAsFailedCheck()
    {
        var report = new WorkflowReport
        {
            StepReports =
            {
                new StepReport
                {
                    IsSuccess = false,
                    StatusCode = HttpStatusCode.InternalServerError,
                    ExpectedStatusCode = HttpStatusCode.OK
                }
            }
        };

        var summary = WorkflowSummary.Create(WorkflowWithSteps(1), report);

        Assert.Equal(0, summary.ChecksPassed);
        Assert.Equal(1, summary.ChecksFailed);
    }

    [Fact]
    public void StepsWithoutResponse_AreExcludedFromHttpMetrics()
    {
        var report = new WorkflowReport
        {
            StepReports = { Step(100), Step(0, success: false, status: null) }
        };

        var summary = WorkflowSummary.Create(WorkflowWithSteps(2), report);

        Assert.Equal(1, summary.HttpRequests);
        Assert.Equal(TimeSpan.FromMilliseconds(100), summary.HttpRequestDuration?.Max);
    }

    [Fact]
    public void DurationStatistics_UseLinearInterpolation()
    {
        var stats = DurationStatistics.From(new[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 }
            .Select(ms => TimeSpan.FromMilliseconds(ms)));

        Assert.NotNull(stats);
        Assert.Equal(55, stats.Average.TotalMilliseconds);
        Assert.Equal(10, stats.Min.TotalMilliseconds);
        Assert.Equal(55, stats.Median.TotalMilliseconds, precision: 3);
        Assert.Equal(100, stats.Max.TotalMilliseconds);
        Assert.Equal(91, stats.P90.TotalMilliseconds, precision: 3);
        Assert.Equal(95.5, stats.P95.TotalMilliseconds, precision: 3);
    }

    [Fact]
    public void DurationStatistics_OfNothing_IsNull()
    {
        Assert.Null(DurationStatistics.From([]));
    }

    [Fact]
    public void AllStepsPassed_IsSuccess()
    {
        var summary = WorkflowSummary.Create(WorkflowWithSteps(2, delays: 1),
            new WorkflowReport { StepReports = { Step(5), Step(7) } });

        Assert.True(summary.IsSuccess);
        Assert.Equal(0, summary.NotRunSteps);
    }
}
