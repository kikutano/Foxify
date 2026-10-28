using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Models;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Protocol;
using Tinyprobe.Core.Reporting;
using Tinyprobe.Core.Tests.Fakes;

namespace Tinyprobe.Core.Tests.Execution;

/// <summary><c>for_each</c> (docs/specs/0011-for-each.md).</summary>
public sealed class ForEachTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tinyprobe-for-each-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private const string Api = """
        base_url: https://api.test
        endpoints:
          create_user:
            method: POST
            path: /users
            params: { email:, firstName: }
            body: { email: "${email}", firstName: "${firstName}" }
          get_user:
            path: /users/${id}
          list:
            path: /users
        """;

    /// <summary>Answers each request with the status of the query or body field `answer`, 200 by default.</summary>
    private static FakeResponse Echo(CapturedRequest request)
    {
        var status = HttpStatusCode.OK;
        if (request.Body is { } body && JsonDocument.Parse(body).RootElement.TryGetProperty("email", out var email))
            status = email.GetString() is "" or null ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.Created;
        return new FakeResponse(status, request.Body ?? $$"""{"path":"{{request.Path}}"}""");
    }

    private static Task<(WorkflowReport Report, FakeServer Server)> RunAsync(string steps, Func<CapturedRequest, FakeResponse>? respond = null) =>
        Run.Workflow($"{Api}\nsteps:\n{steps}", respond ?? Echo);

    private static string[] Names(WorkflowReport report) => report.StepReports.Select(s => s.StepName).ToArray();

    [Fact]
    public async Task EachItem_IsACall_WithItsKeysAsArguments_AndWithForTheRest()
    {
        var (report, server) = await RunAsync("""
              - name: Create
                call: create_user
                for_each:
                  - { email: a@x.test }
                  - { email: b@x.test }
                with:
                  firstName: Ada
            """);

        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess, s.FailureReason));
        Assert.Equal(["""{"email":"a@x.test","firstName":"Ada"}""", """{"email":"b@x.test","firstName":"Ada"}"""],
            server.Requests.Select(r => r.Body));
        Assert.Equal(["Create [1/2]", "Create [2/2]"], Names(report));
        Assert.Equal([(1, 2), (2, 2)], report.StepReports.Select(s => (s.Iteration!.Value, s.Iterations!.Value)));
    }

    [Fact]
    public async Task With_CanReadTheItemsValues()
    {
        var (report, server) = await RunAsync("""
              - call: create_user
                for_each:
                  - { firstName: Ada }
                  - { firstName: Bob }
                with:
                  email: "${firstName}@x.test"
            """);

        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess, s.FailureReason));
        Assert.Equal(["""{"email":"Ada@x.test","firstName":"Ada"}""", """{"email":"Bob@x.test","firstName":"Bob"}"""],
            server.Requests.Select(r => r.Body));
    }

    [Fact]
    public async Task ItemValues_AreVariablesOfTheChecks_AndCanGiveTheStatus()
    {
        var (report, _) = await RunAsync("""
              - call: create_user
                for_each:
                  - { email: a@x.test, firstName: Ada, expected: 201 }
                  - { email: "", firstName: Bob, expected: "422" }
                  - { email: "", firstName: Cy, expected: [400, 422] }
                expect:
                  status: ${expected}
                  body:
                    $.firstName: ${firstName}
            """);

        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess, s.FailureReason));
        Assert.Equal([HttpStatusCode.Created], report.StepReports[0].ExpectedStatusCodes);
        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity], report.StepReports[2].ExpectedStatusCodes);
        Assert.Equal("$.firstName == \"Bob\"", $"{report.StepReports[1].Assertions[0].Subject} {report.StepReports[1].Assertions[0].Condition}");
    }

    [Fact]
    public async Task AStatusThatIsNotACode_FailsTheIteration()
    {
        var (report, _) = await RunAsync("""
              - call: get_user
                for_each: [ { id: 1, expected: ok } ]
                expect: { status: "${expected}" }
            """);

        Assert.Equal("Status ${expected} is not a status code: 'ok'", report.Single().FailureReason);
    }

    [Fact]
    public async Task EveryItemRuns_AfterAFailingOne_ThenTheWorkflowStops()
    {
        var (report, server) = await RunAsync("""
              - call: get_user
                for_each: [ { id: 1 }, { id: 2 }, { id: 3 } ]
                expect:
                  body:
                    $.path: /users/1
              - call: list
            """);

        Assert.Equal([true, false, false], report.StepReports.Select(s => s.IsSuccess));
        Assert.Equal(3, server.Requests.Count);
        Assert.Equal(["get_user id=1 [1/3]", "get_user id=2 [2/3]", "get_user id=3 [3/3]"], Names(report));

        var summary = WorkflowSummary.Create(WorkflowLoader.Parse($"{Api}\nsteps:\n  - call: get_user\n    for_each: [ {{ id: 1 }}, {{ id: 2 }}, {{ id: 3 }} ]\n  - call: list\n", "test.yaml"), report);
        Assert.Equal((4, 1, 2, 1), (summary.TotalSteps, summary.PassedSteps, summary.FailedSteps, summary.NotRunSteps));
    }

    [Fact]
    public async Task Until_AppliesToEachItem()
    {
        var calls = new Dictionary<string, int>();
        var (report, _) = await RunAsync("""
              - call: get_user
                for_each: [ { id: 1 }, { id: 2 } ]
                until:
                  body: { $.ready: true }
                  every: 1ms
                  timeout: 1s
            """, request =>
            {
                calls[request.Path] = calls.GetValueOrDefault(request.Path) + 1;
                return new FakeResponse(Body: calls[request.Path] < 2 ? """{"ready":false}""" : """{"ready":true}""");
            });

        Assert.Equal([2, 2], report.StepReports.Select(s => s.Attempts));
        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess, s.FailureReason));
    }

    [Fact]
    public async Task GeneratedValues_AreNewForEachItem()
    {
        var (_, server) = await RunAsync("""
              - call: create_user
                for_each: [ { email: "${$uuid}" }, { email: "${$uuid}" } ]
                with: { firstName: x }
            """);

        Assert.NotEqual(server.Requests[0].Body, server.Requests[1].Body);
    }

    [Fact]
    public async Task AVariable_IsAListOfObjects_WhoseUnusedKeysAreIgnored()
    {
        var (report, server) = await RunAsync("""
              - call: list
                save: { users: $.users }
              - call: get_user
                for_each: ${users}
                expect:
                  body:
                    $.path: /users/${id}
            """, request => new FakeResponse(Body: request.Path == "/users" && request.Url.EndsWith("/users")
                ? """{"users":[{"id":7,"name":"Ada"},{"id":9,"name":"Bob"}]}"""
                : $$"""{"path":"{{request.Path}}"}"""));

        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess, s.FailureReason));
        Assert.Equal(["/users", "/users/7", "/users/9"], server.Requests.Select(r => r.Path));
    }

    [Theory]
    [InlineData("variables:\n  users: hello\n", "for_each: 'users' is a string, not a list of objects")]
    [InlineData("variables:\n  users: [1, 2]\n", "for_each: item 1 of 'users' is a number, not an object")]
    [InlineData("", "Variable 'users' is not defined.")]
    public async Task AVariableThatIsNotAListOfObjects_FailsTheStep(string variables, string expected)
    {
        var (report, server) = await Run.Workflow($"{Api}\n{variables}steps:\n  - call: get_user\n    for_each: ${{users}}\n", Echo);

        Assert.Equal(expected, report.Single().FailureReason);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task AnEmptyList_RunsNothing_AndPasses()
    {
        var (report, server) = await RunAsync("  - call: get_user\n    for_each: []\n  - call: list\n");

        Assert.Equal(["get_user [no items]", "list"], Names(report));
        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess));
        Assert.Single(server.Requests);
    }

    // --- data files ------------------------------------------------------------

    private async Task<(WorkflowReport Report, FakeServer Server)> RunFileAsync(string data, string extension, string steps)
    {
        Directory.CreateDirectory(Path.Combine(_directory, "data"));
        File.WriteAllText(Path.Combine(_directory, "data", $"cases.{extension}"), data);
        var path = Path.Combine(_directory, "workflows", "test.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"{Api}\nsteps:\n{steps}");

        var server = new FakeServer(Echo);
        return (await new WorkflowEngine(new HttpClient(server)).ExecuteAsync(WorkflowLoader.Load(path)), server);
    }

    [Fact]
    public async Task ACsvFile_GivesOneItemPerLine_WithTextValues()
    {
        var (report, server) = await RunFileAsync(
            "email,firstName,status\r\na@x.test,\"Ada, the first\",201\n\n\"\",\"Bo \"\"B\"\"\",422\n", "csv", """
              - call: create_user
                for_each: { file: ../data/cases.csv }
                expect: { status: "${status}" }
            """);

        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess, s.FailureReason));
        Assert.Equal(["""{"email":"a@x.test","firstName":"Ada, the first"}""", """{"email":"","firstName":"Bo \u0022B\u0022"}"""],
            server.Requests.Select(r => r.Body));
    }

    [Fact]
    public async Task AJsonFile_KeepsTheTypesOfItsValues()
    {
        var (report, server) = await RunFileAsync("""[ { "id": 1 }, { "id": 2 } ]""", "json", """
              - call: get_user
                for_each: { file: ../data/cases.json }
            """);

        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess, s.FailureReason));
        Assert.Equal(["/users/1", "/users/2"], server.Requests.Select(r => r.Path));
        Assert.Equal("get_user id=2 [2/2]", report.StepReports[1].StepName);
    }

    // --- validation ------------------------------------------------------------

    [Theory]
    [InlineData("    for_each: hello", "test.yaml:6:15: for_each must be a list of items, a variable (${cases}) or { file: path }")]
    [InlineData("    for_each: [ { id: 1 }, 2 ]", "test.yaml:6:28: item 2 of for_each must be a mapping of arguments ({ email: x })")]
    [InlineData("    for_each: [ { id: 1, idd: 2 } ]", "test.yaml:6:31: unknown argument 'idd' for endpoint 'get_user' (did you mean 'id'?)")]
    [InlineData("    for_each: [ { id: 1 } ]\n    with: { id: 2 }", "test.yaml:6:19: item 1 of for_each and with both set 'id': keep one")]
    [InlineData("    for_each: [ { id: 1 } ]\n    save: { a: $.id }", "test.yaml:7:11: save cannot be used with for_each: every iteration would overwrite the value")]
    [InlineData("    for_each: { file: missing.csv }", "test.yaml:6:23: cannot read the for_each file missing.csv: not found")]
    [InlineData("    for_each: { files: a.csv }", "test.yaml:6:17: unknown key 'files' in for_each (did you mean 'file'?)")]
    [InlineData("    with: { id: 1 }\n    expect: { status: \"${code}\" }", "test.yaml:7:23: status must be a code, or a placeholder in a for_each step")]
    [InlineData("    for_each: [ { id: 1 } ]\n    until: { status: \"${code}\" }", "test.yaml:7:22: until.status must be a code or a list of codes")]
    public void InvalidForEach_IsReportedWithItsLocation(string step, string expected)
    {
        var errors = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Parse(
            $"base_url: https://api.test\nendpoints:\n  get_user: {{ path: \"/users/${{id}}\" }}\nsteps:\n  - call: get_user\n{step}\n",
            Path.Combine(_directory, "test.yaml"))).Errors;

        Assert.Contains(expected, errors.Select(e => e.Replace(_directory + Path.DirectorySeparatorChar, "")));
    }

    [Fact]
    public void RequiredParams_MayComeFromEveryItem()
    {
        WorkflowLoader.Parse($"{Api}\nsteps:\n  - call: create_user\n    for_each: [ {{ email: a, firstName: b }} ]\n", "test.yaml");
        var errors = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Parse(
            $"{Api}\nsteps:\n  - call: create_user\n    for_each: [ {{ email: a, firstName: b }}, {{ email: c }} ]\n", "test.yaml")).Errors;

        Assert.Contains(errors, e => e.Contains("missing required param 'firstName' of endpoint 'create_user'"));
    }

    [Fact]
    public void AJsonFileThatIsNotAListOfObjects_IsALoadError()
    {
        File.WriteAllText(Path.Combine(_directory, "cases.json"), """{ "id": 1 }""");
        var errors = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Parse(
            $"{Api}\nsteps:\n  - call: get_user\n    for_each: {{ file: cases.json }}\n", Path.Combine(_directory, "test.yaml"))).Errors;

        Assert.Contains(errors, e => e.EndsWith("cases.json must be a list of objects"));
    }

    // --- protocol and reports --------------------------------------------------

    [Fact]
    public async Task StepCompleted_HasEveryIteration_AndShowsTheFirstFailingOne()
    {
        var (report, _) = await RunAsync("""
              - call: get_user
                for_each: [ { id: 1 }, { id: 2 }, { id: 3 } ]
                expect:
                  body:
                    $.path: /users/1
            """);

        var json = JsonDocument.Parse(ProtocolJson.StepCompleted(report.StepReports)).RootElement;
        Assert.Equal("get_user id=2 [2/3]", json.GetProperty("name").GetString());
        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal([1, 2, 3], json.GetProperty("iterations").EnumerateArray().Select(i => i.GetProperty("iteration").GetInt32()));
        Assert.Equal([true, false, false], json.GetProperty("iterations").EnumerateArray().Select(i => i.GetProperty("success").GetBoolean()));

        var plain = JsonDocument.Parse(ProtocolJson.StepCompleted(new StepReport { StepName = "x" })).RootElement;
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("iterations").ValueKind);
    }

    [Fact]
    public void StepStarted_HasTheIteration()
    {
        var json = JsonDocument.Parse(ProtocolJson.StepStarted(new StepProgress(2, "x [1/3]", Iteration: 1, Iterations: 3))).RootElement;
        Assert.Equal((1, 3), (json.GetProperty("iteration").GetInt32(), json.GetProperty("iterations").GetInt32()));

        var plain = JsonDocument.Parse(ProtocolJson.StepStarted(new StepProgress(2, "x"))).RootElement;
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("iteration").ValueKind);
    }

    [Fact]
    public void Inspection_ShowsTheItems_AndAStatusPlaceholder()
    {
        var json = JsonDocument.Parse(ProtocolJson.Inspection("1.2.3", WorkflowLoader.Parse($$"""
            {{Api}}
            variables:
              users: []
            steps:
              - call: get_user
                for_each: [ { id: 1, code: 200 } ]
                expect: { status: "${code}" }
              - call: get_user
                for_each: ${users}
            """, "test.yaml"), indented: false)).RootElement;

        var steps = json.GetProperty("steps");
        Assert.Equal("""{"items":[{"id":1,"code":200}],"variable":null,"file":null}""", steps[0].GetProperty("for_each").GetRawText());
        Assert.Equal("${code}", steps[0].GetProperty("expect").GetProperty("status_placeholder").GetString());
        Assert.Equal("""{"items":null,"variable":"users","file":null}""", steps[1].GetProperty("for_each").GetRawText());
    }

    [Fact]
    public async Task Junit_HasATestCasePerIteration()
    {
        const string yaml = $"{Api}\nsteps:\n  - call: get_user\n    for_each: [ {{ id: 1 }}, {{ id: 2 }} ]\n";
        var workflow = WorkflowLoader.Parse(yaml, "test.yaml");
        var (report, _) = await Run.Workflow(yaml, Echo);

        var xml = XDocument.Parse(JunitReport.Write("1.2.3", [new JunitRun("test.yaml", workflow, report, TimeSpan.Zero, DateTime.UtcNow)]));
        Assert.Equal(["get_user id=1 [1/2]", "get_user id=2 [2/2]"], xml.Descendants("testcase").Select(c => (string)c.Attribute("name")!));
    }
}
