using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Protocol;
using Tinyprobe.Core.Reporting;
using Tinyprobe.Core.Tests.Fakes;

namespace Tinyprobe.Core.Tests.Execution;

/// <summary><c>run --to</c> (docs/specs/0014-run-to-here.md).</summary>
public sealed class RunToTests
{
    private const string Yaml = """
        name: Orders
        base_url: https://api.test
        endpoints:
          login: { method: POST, path: /login }
          order: { path: "/orders/${id}" }
          broken: { path: /broken }
        steps:
          - name: Log in
            call: login
          - sleep: 1ms
          - name: Read an order
            call: order
            for_each: [ { id: 1 }, { id: 2 } ]
          - call: broken
          - name: Log in
            call: login
        """;

    private static WorkflowDefinition UpTo(string step)
    {
        Assert.True(WorkflowLoader.TryStopAfter(WorkflowLoader.Parse(Yaml, "test.yaml"), step, out var upTo, out var error), error);
        return upTo;
    }

    private static string Error(string step)
    {
        Assert.False(WorkflowLoader.TryStopAfter(WorkflowLoader.Parse(Yaml, "test.yaml"), step, out _, out var error));
        return error;
    }

    [Fact]
    public void AStepNumber_KeepsTheStepsUpToIt_ThatOneIncluded()
    {
        var workflow = UpTo("3");

        Assert.Equal(3, workflow.Steps.Count);
        Assert.Equal(2, workflow.StoppedAfter);
        Assert.Equal(5, workflow.StepsInFile);
    }

    [Fact]
    public void AStepName_IsMatchedExactly()
    {
        Assert.Equal(2, UpTo("Read an order").StoppedAfter);
        Assert.Equal(1, UpTo("sleep 1ms").StoppedAfter);
    }

    [Theory]
    [InlineData("6", "--to 6: the workflow has 5 steps")]
    [InlineData("0", "--to 0: the workflow has 5 steps")]
    [InlineData("Read a order", "--to \"Read a order\": no step has this name (did you mean 'Read an order'?)")]
    [InlineData("Log in", "--to \"Log in\": 2 steps have this name (1, 5): use the number")]
    public void AStepThatIsNotThere_OrNotOne_IsAnError(string step, string expected) =>
        Assert.Equal(expected, Error(step));

    [Fact]
    public async Task TheRun_StopsAfterTheStep_AndPassesWhenEveryStepRunPassed()
    {
        var workflow = UpTo("Read an order");
        var server = new FakeServer(r => r.Path == "/broken" ? new FakeResponse(HttpStatusCode.InternalServerError) : new FakeResponse());

        var report = await new WorkflowEngine(new HttpClient(server)).ExecuteAsync(workflow);
        var summary = WorkflowSummary.Create(workflow, report);

        Assert.Equal(["/login", "/orders/1", "/orders/2"], server.Requests.Select(r => r.Path));
        Assert.True(summary.IsSuccess);
        Assert.Equal((3, 3, 0), (summary.TotalSteps, summary.PassedSteps, summary.NotRunSteps));
        Assert.Equal(2, summary.StoppedAfter);

        var finished = JsonDocument.Parse(ProtocolJson.RunFinished(summary, TimeSpan.Zero)).RootElement;
        Assert.Equal(2, finished.GetProperty("summary").GetProperty("stopped_after").GetInt32());
        var started = JsonDocument.Parse(ProtocolJson.RunStarted("1.2.3", workflow, null)).RootElement;
        Assert.Equal(2, started.GetProperty("to").GetInt32());
        Assert.Equal(3, started.GetProperty("steps").GetArrayLength());

        var xml = XDocument.Parse(JunitReport.Write("1.2.3", [new JunitRun("test.yaml", workflow, report, TimeSpan.Zero, DateTime.UtcNow)]));
        Assert.Equal(3, xml.Descendants("testcase").Count());
        Assert.Empty(xml.Descendants("skipped"));
    }

    [Fact]
    public async Task AFailingStepBeforeTheTarget_FailsTheRun()
    {
        var workflow = UpTo("4");
        var server = new FakeServer(r => r.Path == "/orders/2" ? new FakeResponse(HttpStatusCode.NotFound) : new FakeResponse());

        var report = await new WorkflowEngine(new HttpClient(server)).ExecuteAsync(workflow);
        var summary = WorkflowSummary.Create(workflow, report);

        Assert.False(summary.IsSuccess);
        Assert.Equal(1, summary.NotRunSteps); // `broken`, the target, never ran
    }

    [Fact]
    public void AWholeRun_HasNoStop()
    {
        var workflow = WorkflowLoader.Parse(Yaml, "test.yaml");
        var summary = WorkflowSummary.Create(workflow, new Models.WorkflowReport());

        Assert.Null(summary.StoppedAfter);
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(ProtocolJson.RunStarted("1.2.3", workflow, null)).RootElement.GetProperty("to").ValueKind);
    }
}
