using System.Net;
using System.Text;
using System.Text.Json;
using Tinyprobe.Core.Parsing;

namespace Tinyprobe.Cli.Tests;

public sealed class RunCommandTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tinyprobe-suite-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>Answers 200 to /ok and 500 to anything else, and counts the requests.</summary>
    private sealed class Server
    {
        public int Requests;
        public int Handlers;

        public HttpMessageHandler CreateHandler()
        {
            Handlers++;
            return new Handler(this);
        }

        private sealed class Handler(Server server) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref server.Requests);
                var status = request.RequestUri!.AbsolutePath == "/ok" ? HttpStatusCode.OK : HttpStatusCode.InternalServerError;
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json")
                });
            }
        }
    }

    private string Write(string name, string steps)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, $"""
            name: {Path.GetFileNameWithoutExtension(name)}
            base_url: https://api.test
            endpoints:
              ok: {"{"} path: /ok {"}"}
              broken: {"{"} path: /broken {"}"}
            steps:
            {steps}
            """);
        return path;
    }

    private static async Task<(int Exit, List<JsonElement> Events)> Run(Server server, params string[] files)
    {
        var output = new StringWriter();
        var exit = await RunCommand.RunSuiteAsync(
            new NdjsonReporter(output, "1.2.3"), "1.2.3", files[0], files, envPath: null,
            new Dictionary<string, JsonElement>(), TextWriter.Null, recordExchanges: true, server.CreateHandler);
        var events = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToList();
        return (exit, events);
    }

    private static IEnumerable<string?> Names(List<JsonElement> events) => events.Select(e => e.GetProperty("event").GetString());

    [Fact]
    public async Task EveryWorkflowRuns_AfterAFailingOne()
    {
        var failing = Write("a.yaml", "  - call: broken\n  - call: ok\n");
        var passing = Write("b.yaml", "  - call: ok\n");
        var server = new Server();

        var (exit, events) = await Run(server, failing, passing);

        Assert.Equal(1, exit);
        Assert.Equal(
            ["suite_started",
             "run_started", "step_started", "step_completed", "run_finished",
             "run_started", "step_started", "step_completed", "run_finished",
             "suite_finished"],
            Names(events));
        Assert.Equal(2, server.Requests);
        Assert.Equal(2, server.Handlers); // one per workflow: nothing is shared between them

        var finished = events[^1];
        Assert.False(finished.GetProperty("success").GetBoolean());
        Assert.Equal(1, finished.GetProperty("summary").GetProperty("not_run_steps").GetInt32());
    }

    [Fact]
    public async Task PassingSuite_ExitsZero()
    {
        var (exit, events) = await Run(new Server(), Write("a.yaml", "  - call: ok\n"), Write("b.yaml", "  - call: ok\n"));

        Assert.Equal(0, exit);
        Assert.True(events[^1].GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task OneInvalidWorkflow_SendsNothing_AndListsEveryProblem()
    {
        var valid = Write("a.yaml", "  - call: ok\n");
        var invalid = Write("b.yaml", "  - call: okk\n  - call: brokn\n");
        var server = new Server();

        var (exit, events) = await Run(server, valid, invalid);

        Assert.Equal(2, exit);
        Assert.Equal(0, server.Requests);
        var single = Assert.Single(events);
        Assert.Equal("invalid", single.GetProperty("event").GetString());
        Assert.Equal(2, single.GetProperty("errors").GetArrayLength());
        Assert.All(single.GetProperty("errors").EnumerateArray(),
            e => Assert.EndsWith("b.yaml", e.GetProperty("file").GetString()));
    }
}
