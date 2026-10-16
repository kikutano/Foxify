using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Foxify.Core.Execution;
using Foxify.Core.Parsing;
using Foxify.Core.Tests.Fakes;

namespace Foxify.Core.Tests.Execution;

public class UntilTests
{
    private const string Api = """
        base_url: https://api.test
        endpoints:
          get_booking:
            path: /bookings/${id}
            params: { id: }
        """;

    /// <summary>Answers "Pending" for the first <paramref name="pending"/> calls, then "Confirmed".</summary>
    private static Func<CapturedRequest, FakeResponse> BecomesConfirmedAfter(int pending)
    {
        int calls = 0;
        return _ => ++calls <= pending
            ? new FakeResponse(Body: """{ "status": "Pending" }""")
            : new FakeResponse(Body: """{ "status": "Confirmed", "id": 7 }""");
    }

    [Fact]
    public async Task Until_RepeatsTheCallUntilTheBodyCheckPasses()
    {
        var (report, server) = await Run.Workflow($$"""
            {{Api}}
            steps:
              - call: get_booking
                with: { id: 7 }
                until:
                  body:
                    $.status: { in: [Confirmed, Rejected] }
                  every: 20ms
                  timeout: 2s
                expect:
                  body:
                    $.status: Confirmed
                save:
                  bookingId: $.id
            """,
            respond: BecomesConfirmedAfter(2));

        var step = report.Single();
        Assert.True(step.IsSuccess);
        Assert.Equal(3, step.Attempts);
        Assert.Equal(3, server.Requests.Count);
        Assert.Equal("$.status", step.Assertions.Single().Subject);   // expect, evaluated on the last response only
    }

    [Fact]
    public async Task Until_GivesUpAfterTheTimeout_ShowingTheLastValues()
    {
        var (report, server) = await Run.Workflow($$"""
            {{Api}}
            steps:
              - call: get_booking
                with: { id: 7 }
                until:
                  body:
                    $.status: Confirmed
                  every: 50ms
                  timeout: 200ms
            """,
            respond: BecomesConfirmedAfter(1000));

        var step = report.Single();
        Assert.False(step.IsSuccess);
        Assert.StartsWith("Gave up after", step.FailureReason);
        Assert.InRange(server.Requests.Count, 2, 5);
        Assert.Equal(server.Requests.Count, step.Attempts);
        var check = step.Assertions.Single();
        Assert.False(check.Passed);
        Assert.Equal("\"Pending\"", check.ActualValue);
    }

    [Fact]
    public async Task Until_CanWaitForAStatusCode()
    {
        int calls = 0;
        var (report, _) = await Run.Workflow($$"""
            {{Api}}
            steps:
              - call: get_booking
                with: { id: 7 }
                until:
                  status: 200
                  every: 20ms
            """,
            respond: _ => ++calls < 3 ? new FakeResponse(HttpStatusCode.NotFound) : new FakeResponse());

        Assert.True(report.Single().IsSuccess);
        Assert.Equal(3, report.Single().Attempts);
    }

    [Fact]
    public async Task Until_KeepsPollingOnANonJsonBody()
    {
        int calls = 0;
        var (report, _) = await Run.Workflow($$"""
            {{Api}}
            steps:
              - call: get_booking
                with: { id: 7 }
                until:
                  body: { $.ready: true }
                  every: 20ms
            """,
            respond: _ => ++calls < 2 ? new FakeResponse(Body: "<html>starting</html>") : new FakeResponse(Body: """{ "ready": true }"""));

        Assert.True(report.Single().IsSuccess);
        Assert.Equal(2, report.Single().Attempts);
    }

    [Fact]
    public async Task Until_ReportsEachNewAttempt()
    {
        var workflow = WorkflowLoader.Parse($$"""
            {{Api}}
            steps:
              - name: Wait
                call: get_booking
                with: { id: 7 }
                until: { body: { $.status: Confirmed }, every: 10ms }
            """, "test.yaml");
        var engine = new WorkflowEngine(new HttpClient(new FakeServer(BecomesConfirmedAfter(2))));
        var started = new List<string>();
        engine.StepStarted += started.Add;

        await engine.ExecuteAsync(workflow);

        Assert.Equal(["Wait", "Wait (attempt 2)", "Wait (attempt 3)"], started);
    }
}

public class GeneratedValuesTests
{
    private static async Task<List<JsonElement>> Bodies(string yaml, Dictionary<string, string>? environment = null)
    {
        var (report, server) = await Run.Workflow(yaml, environment: environment);
        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess, s.FailureReason));
        return server.Requests.Select(r => Run.Json(r.Body!)).ToList();
    }

    [Fact]
    public async Task EveryUse_GeneratesANewValue()
    {
        var bodies = await Bodies("""
            base_url: https://api.test
            endpoints:
              create: { method: POST, path: /items, body: { a: "${$uuid}", b: "${$uuid}" } }
            steps:
              - call: create
              - call: create
            """);

        var values = bodies.SelectMany(b => new[] { b.GetProperty("a").GetString(), b.GetProperty("b").GetString() }).ToList();
        Assert.All(values, v => Assert.True(Guid.TryParse(v, out _)));
        Assert.Equal(4, values.Distinct().Count());
    }

    [Fact]
    public async Task AVariable_IsGeneratedOncePerRun()
    {
        var bodies = await Bodies("""
            base_url: https://api.test
            variables:
              email: "smoke-${$randomString}@${domain}"
            endpoints:
              create: { method: POST, path: /users, body: { email: "${email}" } }
            steps:
              - call: create
              - call: create
            """,
            environment: new() { ["domain"] = "example.com" });

        var first = bodies[0].GetProperty("email").GetString();
        Assert.Matches(@"^smoke-[a-z0-9]{8}@example\.com$", first);
        Assert.Equal(first, bodies[1].GetProperty("email").GetString());
    }

    [Fact]
    public async Task Generators_HaveTheirFormatAndType()
    {
        var before = DateTimeOffset.UtcNow;
        var body = (await Bodies("""
            base_url: https://api.test
            endpoints:
              create:
                method: POST
                path: /items
                body:
                  timestamp: ${$timestamp}
                  number: ${$randomInt}
                  text: ${$randomString}
                  now: ${$now}
                  later: ${$now+2h}
                  earlier: ${$now-30m}
                  tomorrow: ${$today+1d}
            steps:
              - call: create
            """)).Single();

        Assert.Equal(JsonValueKind.Number, body.GetProperty("timestamp").ValueKind);
        Assert.InRange(body.GetProperty("timestamp").GetInt64(), before.ToUnixTimeSeconds() - 1, before.ToUnixTimeSeconds() + 5);
        Assert.InRange(body.GetProperty("number").GetInt32(), 0, 999_999);
        Assert.Matches("^[a-z0-9]{8}$", body.GetProperty("text").GetString()!);

        var now = DateTimeOffset.Parse(body.GetProperty("now").GetString()!, CultureInfo.InvariantCulture);
        var later = DateTimeOffset.Parse(body.GetProperty("later").GetString()!, CultureInfo.InvariantCulture);
        var earlier = DateTimeOffset.Parse(body.GetProperty("earlier").GetString()!, CultureInfo.InvariantCulture);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", body.GetProperty("now").GetString()!);
        Assert.InRange((later - now).TotalMinutes, 119, 121);
        Assert.InRange((now - earlier).TotalMinutes, 29, 31);
        Assert.Equal(before.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), body.GetProperty("tomorrow").GetString());
    }

    [Fact]
    public async Task AVariableThatRefersToItself_FailsTheStep()
    {
        var (report, _) = await Run.Workflow("""
            base_url: https://api.test
            variables:
              a: "${b}"
              b: "${a}"
            endpoints:
              create: { method: POST, path: /items, body: { a: "${a}" } }
            steps:
              - call: create
            """);

        Assert.False(report.Single().IsSuccess);
        Assert.Contains("refers to itself", report.Single().FailureReason);
    }
}
