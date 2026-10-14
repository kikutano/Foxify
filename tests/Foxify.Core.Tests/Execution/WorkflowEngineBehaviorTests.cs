using System.Globalization;
using System.Net;
using Foxify.Core.Execution;
using Foxify.Core.Models;

namespace Foxify.Core.Tests.Execution;

public class WorkflowEngineBehaviorTests
{
    private sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string? ContentType, string? Body);

    /// <summary>Answers every request with a fresh response and records what was sent.</summary>
    private sealed class RecordingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method, request.RequestUri, request.Content?.Headers.ContentType?.ToString(), body));

            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }

    private static (WorkflowEngine Engine, RecordingHandler Handler) CreateEngine(
        string responseBody = "{}", HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new RecordingHandler(status, responseBody);
        var engine = new WorkflowEngine(new HttpClient(handler), TextWriter.Null);
        return (engine, handler);
    }

    private static WorkflowTemplate SingleFunction(FunctionDefinition function, string baseUrl = "https://api.test")
    {
        return new WorkflowTemplate
        {
            Metadata = new WorkflowMetadata { Name = "Test" },
            Functions = new() { ["Call"] = function },
            Workflow = new() { new WorkflowStep { Type = "function", FunctionName = "Call" } },
            EnvironmentVariables = new() { ["baseUrl"] = baseUrl }
        };
    }

    [Fact]
    public async Task Patch_IsSentAsPatch()
    {
        var (engine, handler) = CreateEngine();

        var report = await engine.ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition
        {
            Method = "patch",
            Endpoint = "/users/1",
            Body = "{}"
        }));

        Assert.True(report.StepReports.Single().IsSuccess);
        Assert.Equal(HttpMethod.Patch, handler.Requests.Single().Method);
    }

    [Theory]
    [InlineData("https://api.test", "/v1/users", "https://api.test/v1/users")]
    [InlineData("https://api.test/", "users", "https://api.test/users")]
    [InlineData("https://api.test/v2/", "/users", "https://api.test/v2/users")]
    [InlineData("https://api.test", "https://other.test/ping", "https://other.test/ping")]
    public async Task RequestUri_CombinesBaseUrlAndEndpoint(string baseUrl, string endpoint, string expected)
    {
        var (engine, handler) = CreateEngine();

        await engine.ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition { Endpoint = endpoint }, baseUrl));

        Assert.Equal(expected, handler.Requests.Single().Uri?.ToString());
    }

    [Fact]
    public async Task FunctionBaseurl_OverridesEnvironmentBaseUrl()
    {
        var (engine, handler) = CreateEngine();
        engine.SetVariable("otherUrl", "https://other.test");

        await engine.ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition
        {
            Baseurl = "${otherUrl}",
            Endpoint = "/ping"
        }));

        Assert.Equal("https://other.test/ping", handler.Requests.Single().Uri?.ToString());
    }

    [Fact]
    public async Task ContentTypeHeader_IsAppliedToBody()
    {
        var (engine, handler) = CreateEngine();

        await engine.ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition
        {
            Method = "POST",
            Endpoint = "/items",
            Headers = new() { ["Content-Type"] = "application/json; charset=utf-8" },
            Body = """{ "name": "foxify" }"""
        }));

        var request = handler.Requests.Single();
        Assert.Equal("application/json; charset=utf-8", request.ContentType);
        Assert.Equal("""{ "name": "foxify" }""", request.Body);
    }

    [Fact]
    public async Task UndefinedVariable_FailsTheStepWithoutSending()
    {
        var (engine, handler) = CreateEngine();

        var report = await engine.ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition
        {
            Endpoint = "/users",
            Headers = new() { ["Authorization"] = "Bearer ${token}" }
        }));

        Assert.False(report.StepReports.Single().IsSuccess);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnexpectedStatusCode_FailsTheStep()
    {
        var (engine, _) = CreateEngine(status: HttpStatusCode.NotFound);

        var report = await engine.ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition
        {
            Endpoint = "/users/1",
            Expected = new() { ["status_code"] = "200" }
        }));

        var step = report.StepReports.Single();
        Assert.False(step.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, step.StatusCode);
        Assert.Equal(HttpStatusCode.OK, step.ExpectedStatusCode);
        Assert.Contains("404", step.FailureReason);
    }

    [Fact]
    public async Task Report_DescribesTheRequestAndResponse()
    {
        var (engine, _) = CreateEngine(responseBody: """{ "id": 1 }""", status: HttpStatusCode.Created);

        var report = await engine.ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition
        {
            Method = "POST",
            Endpoint = "/users?notify=true",
            Body = """{ "name": "ada" }""",
            Expected = new() { ["status_code"] = "201" }
        }));

        var step = report.StepReports.Single();
        Assert.True(step.IsSuccess);
        Assert.Equal("Call", step.StepName);
        Assert.Equal("POST", step.Method);
        Assert.Equal("https://api.test/users?notify=true", step.Url?.ToString());
        Assert.Equal(HttpStatusCode.Created, step.StatusCode);
        Assert.Equal(17, step.BytesSent);
        Assert.Equal(11, step.BytesReceived);
        Assert.True(step.Duration > TimeSpan.Zero);
        Assert.Null(step.FailureReason);
    }

    [Fact]
    public async Task AllAssertions_AreEvaluatedAndReported()
    {
        var (engine, _) = CreateEngine(responseBody: """{ "a": 1, "b": 2, "c": 3 }""");

        var report = await engine.ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition
        {
            Endpoint = "/data",
            Extract = new() { ["a"] = "$.a", ["b"] = "$.b", ["c"] = "$.c" },
            Asserts = new() { ["a"] = "== 1", ["b"] = "== 99", ["c"] = "== 3" }
        }));

        var step = report.StepReports.Single();
        Assert.False(step.IsSuccess);
        Assert.Collection(step.Assertions,
            a => Assert.True(a.Passed),
            b => { Assert.False(b.Passed); Assert.Equal("2", b.ActualValue); Assert.Equal("== 99", b.Expression); },
            c => Assert.True(c.Passed));
    }

    [Fact]
    public async Task NumericVariables_AreFormattedIndependentlyOfTheCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("it-IT");
        try
        {
            var (engine, handler) = CreateEngine();
            engine.SetVariable("price", 9.9);

            await engine.ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition { Endpoint = "/items?max=${price}" }));

            Assert.Equal("https://api.test/items?max=9.9", handler.Requests.Single().Uri?.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Events_AreRaisedForEveryStep()
    {
        var (engine, _) = CreateEngine();
        var workflow = SingleFunction(new FunctionDefinition { Endpoint = "/a" });
        workflow.Workflow.Insert(0, new WorkflowStep { Type = "DELAY", DurationSeconds = 0.01 });
        workflow.Workflow.Add(new WorkflowStep { Type = "function", FunctionName = "Call", Name = "Again" });

        var started = new List<string>();
        var completed = new List<string>();
        engine.StepStarted += started.Add;
        engine.StepCompleted += r => completed.Add(r.StepName);

        await engine.ExecuteWorkflowAsync(workflow);

        Assert.Equal(["delay 0.01s", "Call", "Again"], started);
        Assert.Equal(["Call", "Again"], completed);
    }

    [Fact]
    public async Task ExtractFromNonJsonResponse_FailsTheStep()
    {
        var (engine, _) = CreateEngine(responseBody: "<html>oops</html>");

        var report = await engine.ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition
        {
            Endpoint = "/users/1",
            Extract = new() { ["id"] = "$.id" }
        }));

        Assert.False(report.StepReports.Single().IsSuccess);
    }

    [Theory]
    [InlineData("$.user.name", "== 'Ada'")]
    [InlineData("$.items[1].id", "== 2")]
    [InlineData("$.matrix[1][0]", "== 3")]
    [InlineData("$.active", "== true")]
    [InlineData("$.price", "== 9.90")]
    [InlineData("$.missing", "== null")]
    public async Task ExtractedValues_CanBeAsserted(string path, string assertion)
    {
        var (engine, _) = CreateEngine(responseBody: """
            {
              "user": { "name": "Ada" },
              "items": [ { "id": 1 }, { "id": 2 } ],
              "matrix": [ [1, 2], [3, 4] ],
              "active": true,
              "price": 9.90,
              "missing": null
            }
            """);

        var report = await engine.ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition
        {
            Endpoint = "/data",
            Extract = new() { ["value"] = path },
            Asserts = new() { ["value"] = assertion }
        }));

        Assert.True(report.StepReports.Single().IsSuccess);
    }

    [Fact]
    public async Task ExtractedVariable_IsUsedByNextStep()
    {
        var (engine, handler) = CreateEngine(responseBody: """{ "token": "abc", "id": 42 }""");
        var workflow = SingleFunction(new FunctionDefinition
        {
            Endpoint = "/login",
            Extract = new() { ["userId"] = "$.id" }
        });
        workflow.Functions["Next"] = new FunctionDefinition { Endpoint = "/users/${userId}" };
        workflow.Workflow.Add(new WorkflowStep { Type = "function", FunctionName = "Next", DependsOn = ["Call"] });

        var report = await engine.ExecuteWorkflowAsync(workflow);

        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess));
        Assert.Equal("https://api.test/users/42", handler.Requests[1].Uri?.ToString());
    }

    [Fact]
    public async Task FailingStep_StopsTheWorkflow()
    {
        var (engine, handler) = CreateEngine(status: HttpStatusCode.InternalServerError);
        var workflow = SingleFunction(new FunctionDefinition
        {
            Endpoint = "/first",
            Expected = new() { ["status_code"] = "200" }
        });
        workflow.Workflow.Add(new WorkflowStep { Type = "function", FunctionName = "Call" });

        var report = await engine.ExecuteWorkflowAsync(workflow);

        Assert.Single(report.StepReports);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("Missing", null)]
    [InlineData("Call", "NeverRan")]
    public async Task MissingFunctionOrDependency_FailsTheStep(string functionName, string? dependency)
    {
        var (engine, handler) = CreateEngine();
        var workflow = SingleFunction(new FunctionDefinition { Endpoint = "/x" });
        workflow.Workflow[0] = new WorkflowStep
        {
            Type = "function",
            FunctionName = functionName,
            DependsOn = dependency is null ? null : [dependency]
        };

        var report = await engine.ExecuteWorkflowAsync(workflow);

        Assert.False(report.StepReports.Single().IsSuccess);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EnvironmentVariables_OverrideWorkflowDefaults()
    {
        var (engine, handler) = CreateEngine();
        var workflow = SingleFunction(new FunctionDefinition { Endpoint = "/${resource}/${id}" });
        workflow.Metadata.Variables["resource"] = "users";
        workflow.Metadata.Variables["id"] = "1";
        workflow.Variables["id"] = "2";
        workflow.EnvironmentVariables["id"] = "3";

        await engine.ExecuteWorkflowAsync(workflow);

        Assert.Equal("https://api.test/users/3", handler.Requests.Single().Uri?.ToString());
    }

    /// <summary>Throws a network error for the first <c>failures</c> calls, then answers 200.</summary>
    private sealed class FlakyHandler(int failures) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls <= failures)
                throw new HttpRequestException("Connection refused.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    [Theory]
    [InlineData(2, 2, true, 3)]   // fails twice, 2 retries -> succeeds on the 3rd call
    [InlineData(2, 1, false, 2)]  // fails twice, 1 retry -> gives up after 2 calls
    [InlineData(1, 0, false, 1)]  // no retries
    public async Task TransientErrors_AreRetriedUpToMaxRetries(int failures, int maxRetries, bool success, int calls)
    {
        var handler = new FlakyHandler(failures);
        var engine = new WorkflowEngine(new HttpClient(handler), TextWriter.Null);
        var workflow = SingleFunction(new FunctionDefinition { Endpoint = "/ping" });
        workflow.Settings.MaxRetries = maxRetries;

        var report = await engine.ExecuteWorkflowAsync(workflow);

        Assert.Equal(success, report.StepReports.Single().IsSuccess);
        Assert.Equal(calls, handler.Calls);
    }

    [Fact]
    public async Task HttpErrorStatus_IsNotRetried()
    {
        var (engine, handler) = CreateEngine(status: HttpStatusCode.ServiceUnavailable);
        var workflow = SingleFunction(new FunctionDefinition
        {
            Endpoint = "/ping",
            Expected = new() { ["status_code"] = "503" }
        });
        workflow.Settings.MaxRetries = 3;

        var report = await engine.ExecuteWorkflowAsync(workflow);

        Assert.True(report.StepReports.Single().IsSuccess);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FailureBeforeSending_HasNoStatusCode()
    {
        var (engine, _) = CreateEngine();

        var report = await engine.ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition { Endpoint = "/${undefined}" }));

        Assert.Null(report.StepReports.Single().StatusCode);
    }

    [Fact]
    public async Task Engine_DoesNotDisposeTheCallersHttpClient()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        using var httpClient = new HttpClient(handler);

        await new WorkflowEngine(httpClient, TextWriter.Null)
            .ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition { Endpoint = "/a" }));
        var report = await new WorkflowEngine(httpClient, TextWriter.Null)
            .ExecuteWorkflowAsync(SingleFunction(new FunctionDefinition { Endpoint = "/b" }));

        Assert.True(report.StepReports.Single().IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task UnknownStepType_FailsTheWorkflow()
    {
        var (engine, handler) = CreateEngine();
        var workflow = SingleFunction(new FunctionDefinition { Endpoint = "/x" });
        workflow.Workflow[0].Type = "fuction";

        var report = await engine.ExecuteWorkflowAsync(workflow);

        Assert.False(report.StepReports.Single().IsSuccess);
        Assert.Empty(handler.Requests);
    }
}
