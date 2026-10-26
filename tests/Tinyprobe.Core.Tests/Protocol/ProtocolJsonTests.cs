using System.Net;
using System.Text.Json;
using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Protocol;
using Tinyprobe.Core.Reporting;
using Tinyprobe.Core.Tests.Fakes;

namespace Tinyprobe.Core.Tests.Protocol;

/// <summary>The JSON read by tools (a UI): <c>inspect --json</c> and the <c>ndjson</c> run events.</summary>
public class ProtocolJsonTests
{
    private const string Workflow = """
        name: Users
        base_url: ${baseUrl}
        headers:
          X-Client: tinyprobe
        auth:
          bearer:
            Authorization: "Bearer ${token}"
        endpoints:
          login:
            method: POST
            path: /login
            params:
              email:
              password: secret
            body:
              email: "${email}"
              password: "${password}"
          get_user:
            path: /users/${id}
            query: [fields]
            auth: bearer
            headers:
              X-Client: tinyprobe-ui
        variables:
          email: "smoke-${$uuid}@example.com"
          retries: 3
        steps:
          - name: Login
            call: login
            with:
              email: "${email}"
            expect:
              status: [200, 201]
              body:
                $.token: { exists: true }
                $.roles: { contains: admin }
              duration: { lt: 500ms }
            save:
              token: $.token
          - sleep: 250ms
          - call: get_user
            with: { id: 7 }
            until: { body: { $.status: ready }, every: 100ms, timeout: 2s }
        """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement Inspect(string yaml) =>
        Parse(ProtocolJson.Inspection("1.2.3", WorkflowLoader.Parse(yaml, "users.yaml"), indented: false));

    [Fact]
    public void Inspection_DescribesTheWorkflow()
    {
        var json = Inspect(Workflow);

        Assert.Equal(ProtocolJson.Version, json.GetProperty("protocol").GetInt32());
        Assert.Equal("1.2.3", json.GetProperty("version").GetString());
        Assert.True(json.GetProperty("valid").GetBoolean());
        Assert.Equal("workflow", json.GetProperty("kind").GetString());
        Assert.Equal("Users", json.GetProperty("name").GetString());
        Assert.Equal(30000, json.GetProperty("settings").GetProperty("timeout_ms").GetInt32());
    }

    [Fact]
    public void Inspection_ShowsVariablesUnresolved()
    {
        var variables = Inspect(Workflow).GetProperty("variables").EnumerateArray().ToList();

        Assert.Equal(["email", "retries"], variables.Select(v => v.GetProperty("name").GetString()));
        Assert.Equal("smoke-${$uuid}@example.com", variables[0].GetProperty("value").GetString());
        Assert.Equal(3, variables[1].GetProperty("value").GetInt32());
        Assert.Equal(25, variables[0].GetProperty("location").GetProperty("line").GetInt32());
    }

    [Fact]
    public void Inspection_DescribesEndpoints()
    {
        var endpoints = Inspect(Workflow).GetProperty("endpoints").EnumerateArray().ToList();
        var login = endpoints[0];
        var getUser = endpoints[1];

        Assert.Equal("POST", login.GetProperty("method").GetString());
        Assert.Equal("${baseUrl}", login.GetProperty("base_url").GetString());
        Assert.Equal("""{"email":"${email}","password":"${password}"}""", login.GetProperty("body").GetRawText());
        Assert.Equal("""[{"name":"email","required":true,"default":null},{"name":"password","required":false,"default":"secret"}]""",
            login.GetProperty("params").GetRawText());
        Assert.Equal("""["baseUrl","email","password"]""", login.GetProperty("arguments").GetRawText());

        Assert.Equal("/users/${id}", getUser.GetProperty("path").GetString());
        Assert.Equal("""["fields"]""", getUser.GetProperty("query").GetRawText());
        Assert.Equal("""[{"name":"X-Client","value":"tinyprobe-ui"},{"name":"Authorization","value":"Bearer ${token}"}]""",
            getUser.GetProperty("headers").GetRawText());
        Assert.Equal("""["baseUrl","fields","id","token"]""", getUser.GetProperty("arguments").GetRawText());
        Assert.Equal(JsonValueKind.Null, getUser.GetProperty("body").ValueKind);
    }

    [Fact]
    public void Inspection_DescribesSteps()
    {
        var steps = Inspect(Workflow).GetProperty("steps").EnumerateArray().ToList();

        Assert.Equal(["call", "sleep", "call"], steps.Select(s => s.GetProperty("type").GetString()));
        Assert.Equal([0, 1, 2], steps.Select(s => s.GetProperty("index").GetInt32()));

        var login = steps[0];
        Assert.Equal("Login", login.GetProperty("name").GetString());
        Assert.Equal("""{"email":"${email}"}""", login.GetProperty("with").GetRawText());
        Assert.Equal("[200,201]", login.GetProperty("expect").GetProperty("status").GetRawText());
        var checks = login.GetProperty("expect").GetProperty("checks").EnumerateArray()
            .Select(c => $"{c.GetProperty("subject").GetString()} {c.GetProperty("condition").GetString()}");
        Assert.Equal(["$.token exists", "$.roles contains \"admin\"", "duration < 500ms"], checks);
        Assert.Equal("""{"token":"$.token"}""", login.GetProperty("save").GetRawText());
        Assert.Equal(JsonValueKind.Null, login.GetProperty("until").ValueKind);

        Assert.Equal("sleep 250ms", steps[1].GetProperty("name").GetString());
        Assert.Equal(250, steps[1].GetProperty("duration_ms").GetDouble());

        var until = steps[2].GetProperty("until");
        Assert.Equal("get_user id=7", steps[2].GetProperty("name").GetString());
        Assert.Equal(100, until.GetProperty("every_ms").GetDouble());
        Assert.Equal(2000, until.GetProperty("timeout_ms").GetDouble());
        Assert.Equal("== \"ready\"", until.GetProperty("checks")[0].GetProperty("condition").GetString());
    }

    [Fact]
    public void Inspection_OfAnApiFile_HasNoSteps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tinyprobe-api-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, "endpoints:\n  ping: { path: /ping }\n");
        try
        {
            var json = Parse(ProtocolJson.Inspection("1.2.3", WorkflowLoader.LoadAny(path)));

            Assert.Equal("api", json.GetProperty("kind").GetString());
            Assert.Equal(0, json.GetProperty("steps").GetArrayLength());
            Assert.Equal("ping", json.GetProperty("endpoints")[0].GetProperty("name").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Invalid_ListsProblemsWithTheirPosition()
    {
        var problems = Assert.Throws<WorkflowLoadException>(() =>
            WorkflowLoader.Parse("endpoints:\n  a: { path: /a }\nsteps:\n  - call: b\n", "bad.yaml")).Problems;

        var json = Parse(ProtocolJson.Invalid("1.2.3", "bad.yaml", problems, eventName: "invalid", indented: false));

        Assert.Equal("invalid", json.GetProperty("event").GetString());
        Assert.False(json.GetProperty("valid").GetBoolean());
        var error = json.GetProperty("errors").EnumerateArray().Single();
        Assert.Equal(4, error.GetProperty("line").GetInt32());
        Assert.Equal(5, error.GetProperty("column").GetInt32());
        Assert.Equal("unknown endpoint 'b' (did you mean 'a'?)", error.GetProperty("message").GetString());
        Assert.EndsWith("bad.yaml:4:5: unknown endpoint 'b' (did you mean 'a'?)", error.GetProperty("text").GetString());
    }

    // --- run events ----------------------------------------------------------

    private const string RunApi = """
        base_url: https://api.test
        endpoints:
          login:
            method: POST
            path: /login
            body: { user: ada }
            headers:
              X-Trace: abc
          me:
            path: /me
        """;

    private static async Task<List<string>> RunEvents(string steps, Func<CapturedRequest, FakeResponse>? respond = null)
    {
        var workflow = WorkflowLoader.Parse($"{RunApi}\nsteps:\n{steps}", "run.yaml");
        var engine = new WorkflowEngine(new HttpClient(new FakeServer(respond))) { RecordExchanges = true };
        var lines = new List<string> { ProtocolJson.RunStarted("1.2.3", workflow, environmentFile: null) };
        engine.StepStarted += progress => lines.Add(ProtocolJson.StepStarted(progress));
        engine.StepCompleted += step => lines.Add(ProtocolJson.StepCompleted(step));

        var report = await engine.ExecuteAsync(workflow);
        lines.Add(ProtocolJson.RunFinished(WorkflowSummary.Create(workflow, report), TimeSpan.FromMilliseconds(12.5)));
        return lines;
    }

    private static JsonElement Completed(List<string> lines) =>
        lines.Select(Parse).Single(e => e.GetProperty("event").GetString() == "step_completed");

    [Fact]
    public async Task RunEvents_AreSingleLineJsonInOrder()
    {
        var lines = await RunEvents("  - call: login\n  - sleep: 1ms\n  - call: me\n");

        Assert.All(lines, line => Assert.DoesNotContain('\n', line));
        Assert.Equal(
            ["run_started", "step_started", "step_completed", "step_started", "step_started", "step_completed", "run_finished"],
            lines.Select(l => Parse(l).GetProperty("event").GetString()));
        Assert.Equal([0, 1, 2], lines.Select(Parse).Where(e => e.GetProperty("event").GetString() == "step_started")
            .Select(e => e.GetProperty("index").GetInt32()));

        var started = Parse(lines[0]);
        Assert.Equal(ProtocolJson.Version, started.GetProperty("protocol").GetInt32());
        Assert.Equal("""[{"index":0,"type":"call","name":"login"},{"index":1,"type":"sleep","name":"sleep 1ms"},{"index":2,"type":"call","name":"me"}]""",
            started.GetProperty("steps").GetRawText());
    }

    [Fact]
    public async Task StepStarted_IsRaisedAgainForEachAttemptOfUntil()
    {
        int calls = 0;
        var lines = await RunEvents("  - call: me\n    until: { body: { $.ready: true }, every: 10ms }\n",
            _ => new FakeResponse(Body: ++calls < 3 ? """{"ready":false}""" : """{"ready":true}"""));

        var attempts = lines.Select(Parse).Where(e => e.GetProperty("event").GetString() == "step_started")
            .Select(e => e.GetProperty("attempt").GetInt32());

        Assert.Equal([1, 2, 3], attempts);
        Assert.Equal(3, Completed(lines).GetProperty("attempts").GetInt32());
    }

    [Fact]
    public async Task StepCompleted_HasTheRequestTheResponseAndTheSavedValues()
    {
        var lines = await RunEvents(
            "  - call: login\n    expect: { body: { $.id: 5 } }\n    save: { userId: $.id }\n",
            _ => new FakeResponse(HttpStatusCode.Created, """{"id":5}""") { Headers = { ["X-Request-Id"] = "r1" } });

        var step = Completed(lines);

        Assert.True(step.GetProperty("success").GetBoolean());
        Assert.Equal(201, step.GetProperty("status").GetInt32());
        Assert.Equal("""{"userId":5}""", step.GetProperty("saved").GetRawText());
        Assert.Equal("$.id", step.GetProperty("assertions")[0].GetProperty("subject").GetString());

        var request = step.GetProperty("request");
        Assert.Equal("POST", request.GetProperty("method").GetString());
        Assert.Equal("https://api.test/login", request.GetProperty("url").GetString());
        Assert.Equal("""{"user":"ada"}""", request.GetProperty("body").GetString());
        var requestHeaders = request.GetProperty("headers").EnumerateArray().Select(h => h.GetProperty("name").GetString()).ToList();
        Assert.Contains("X-Trace", requestHeaders);
        Assert.Contains("Content-Type", requestHeaders);

        var response = step.GetProperty("response");
        Assert.Equal(201, response.GetProperty("status").GetInt32());
        Assert.Equal("""{"id":5}""", response.GetProperty("body").GetString());
        Assert.False(response.GetProperty("body_truncated").GetBoolean());
        Assert.Contains(response.GetProperty("headers").EnumerateArray(), h => h.GetProperty("value").GetString() == "r1");
    }

    [Fact]
    public async Task StepCompleted_OfANetworkError_HasTheRequestButNoResponse()
    {
        var lines = await RunEvents("  - call: me\n", _ => new FakeResponse { NetworkError = true });

        var step = Completed(lines);

        Assert.False(step.GetProperty("success").GetBoolean());
        Assert.Equal("Connection refused.", step.GetProperty("failure").GetString());
        Assert.Equal(JsonValueKind.Null, step.GetProperty("status").ValueKind);
        Assert.Equal("GET", step.GetProperty("request").GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Null, step.GetProperty("response").ValueKind);
    }

    [Fact]
    public async Task RunFinished_HasTheSummary()
    {
        var lines = await RunEvents("  - call: me\n  - call: login\n    expect: { status: 404 }\n  - call: me\n");

        var finished = Parse(lines[^1]);
        var summary = finished.GetProperty("summary");

        Assert.False(finished.GetProperty("success").GetBoolean());
        Assert.Equal(3, summary.GetProperty("total_steps").GetInt32());
        Assert.Equal(1, summary.GetProperty("passed_steps").GetInt32());
        Assert.Equal(1, summary.GetProperty("failed_steps").GetInt32());
        Assert.Equal(1, summary.GetProperty("not_run_steps").GetInt32());
        Assert.Equal(2, summary.GetProperty("http_requests").GetInt32());
        Assert.Equal(12.5, summary.GetProperty("duration_ms").GetDouble());
        Assert.Equal(JsonValueKind.Object, summary.GetProperty("http_req_duration_ms").ValueKind);
    }

    [Fact]
    public async Task Exchanges_AreNotRecordedByDefault()
    {
        var report = await new WorkflowEngine(new HttpClient(new FakeServer()))
            .ExecuteAsync(WorkflowLoader.Parse($"{RunApi}\nsteps:\n  - call: me\n", "run.yaml"));

        Assert.Null(report.Single().Request);
        Assert.Null(report.Single().Response);
    }

    [Fact]
    public async Task LargeResponseBodies_AreCut()
    {
        var body = $"\"{new string('x', WorkflowEngine.MaxRecordedBodyBytes)}\"";
        var workflow = WorkflowLoader.Parse($"{RunApi}\nsteps:\n  - call: me\n", "run.yaml");
        var engine = new WorkflowEngine(new HttpClient(new FakeServer(_ => new FakeResponse(Body: body)))) { RecordExchanges = true };

        var response = (await engine.ExecuteAsync(workflow)).Single().Response!;

        Assert.True(response.BodyTruncated);
        Assert.Equal(WorkflowEngine.MaxRecordedBodyBytes, response.Body.Length);
    }

    [Fact]
    public void Invalid_ListsTheEndpointsFound_WhenGiven()
    {
        var api = WorkflowLoader.Parse("base_url: https://api.test\nendpoints:\n  ping: { path: /ping }\nsteps:\n  - call: ping\n", "w.yaml");
        var problems = new List<LoadProblem> { new("w.yaml", 5, 11, "unknown endpoint 'pign' (did you mean 'ping'?)") };

        var withEndpoints = Parse(ProtocolJson.Invalid("1.2.3", "w.yaml", problems, endpoints: api.Endpoints));
        var without = Parse(ProtocolJson.Invalid("1.2.3", "w.yaml", problems));

        Assert.Equal("ping", withEndpoints.GetProperty("endpoints")[0].GetProperty("name").GetString());
        Assert.Equal("/ping", withEndpoints.GetProperty("endpoints")[0].GetProperty("path").GetString());
        Assert.False(without.TryGetProperty("endpoints", out _));
    }

    [Fact]
    public void SuiteEvents_ListTheFiles_AndTheTotals()
    {
        var passed = new Models.WorkflowReport();
        passed.StepReports.Add(new Models.StepReport());
        var failed = new Models.WorkflowReport();
        failed.StepReports.Add(new Models.StepReport { StepName = "Create", IsSuccess = false, FailureReason = "Expected status 201, got 500" });
        var oneStep = new WorkflowDefinition { Steps = [new CallStep { Name = "S", Call = "ping", Location = new("w.yaml", 1, 1) }] };
        var twoSteps = new WorkflowDefinition { Steps = [.. oneStep.Steps, .. oneStep.Steps] };
        var suite = new SuiteSummary([
            SuiteEntry.Create("auth.yaml", oneStep, passed, TimeSpan.FromMilliseconds(5)),
            SuiteEntry.Create("users.yaml", twoSteps, failed, TimeSpan.FromMilliseconds(7))
        ]);

        var started = Parse(ProtocolJson.SuiteStarted("1.2.3", ["auth.yaml", "users.yaml"]));
        var finished = Parse(ProtocolJson.SuiteFinished(suite, TimeSpan.FromMilliseconds(12.5)));

        Assert.Equal("suite_started", started.GetProperty("event").GetString());
        Assert.Equal(ProtocolJson.Version, started.GetProperty("protocol").GetInt32());
        Assert.Equal([Path.GetFullPath("auth.yaml"), Path.GetFullPath("users.yaml")],
            started.GetProperty("files").EnumerateArray().Select(f => f.GetString()));

        Assert.Equal("suite_finished", finished.GetProperty("event").GetString());
        Assert.False(finished.GetProperty("success").GetBoolean());
        Assert.Equal([true, false], finished.GetProperty("workflows").EnumerateArray().Select(w => w.GetProperty("success").GetBoolean()));
        Assert.Equal(Path.GetFullPath("users.yaml"), finished.GetProperty("workflows")[1].GetProperty("file").GetString());
        var summary = finished.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("total_workflows").GetInt32());
        Assert.Equal(1, summary.GetProperty("passed_workflows").GetInt32());
        Assert.Equal(1, summary.GetProperty("failed_workflows").GetInt32());
        Assert.Equal(3, summary.GetProperty("total_steps").GetInt32());
        Assert.Equal(1, summary.GetProperty("passed_steps").GetInt32());
        Assert.Equal(1, summary.GetProperty("failed_steps").GetInt32());
        Assert.Equal(1, summary.GetProperty("not_run_steps").GetInt32());
        Assert.Equal(12.5, summary.GetProperty("duration_ms").GetDouble());
    }
}
