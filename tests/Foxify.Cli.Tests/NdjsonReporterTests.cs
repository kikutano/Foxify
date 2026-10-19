using System.Text;
using System.Text.Json;
using Foxify.Core.Execution;
using Foxify.Core.Parsing;
using Foxify.Core.Reporting;

namespace Foxify.Cli.Tests;

public class NdjsonReporterTests
{
    private sealed class Ok : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json")
            });
    }

    [Fact]
    public async Task Run_WritesOneJsonEventPerLine()
    {
        var workflow = WorkflowLoader.Parse("""
            base_url: https://api.test
            endpoints:
              ping: { path: /ping }
            steps:
              - call: ping
              - call: ping
            """, "ping.yaml");
        var output = new StringWriter();
        var reporter = new NdjsonReporter(output, "1.2.3");
        var engine = new WorkflowEngine(new HttpClient(new Ok())) { RecordExchanges = true };
        var environment = new Dictionary<string, JsonElement>();

        reporter.WriteHeader("1.2.3", "ping.yaml", null, workflow, environment);
        var report = await reporter.RunAsync(engine, workflow, environment);
        reporter.WriteSummary(WorkflowSummary.Create(workflow, report), TimeSpan.FromMilliseconds(5));

        var events = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("event").GetString());
        Assert.Equal(["run_started", "step_started", "step_completed", "step_started", "step_completed", "run_finished"], events);
    }

    [Fact]
    public void LoadErrors_AreAnInvalidEvent()
    {
        var problems = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Parse("steps: []", "empty.yaml")).Problems;
        var output = new StringWriter();

        new NdjsonReporter(output, "1.2.3").WriteLoadErrors("empty.yaml", problems);

        var json = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Equal("invalid", json.GetProperty("event").GetString());
        Assert.Contains("no steps", json.GetProperty("errors")[0].GetProperty("message").GetString());
    }
}
