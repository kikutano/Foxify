using System.Globalization;
using System.Net;
using Tinyprobe.Core.Tests.Fakes;

namespace Tinyprobe.Core.Tests.Execution;

public class WorkflowEngineTests
{
    private const string Api = """
        base_url: https://api.test/v1/
        headers:
          X-Client: tinyprobe
        auth:
          bearer:
            Authorization: "Bearer ${token}"
          apikey:
            X-API-Key: ${apiKey}
        """;

    // --- Requests --------------------------------------------------------------

    [Fact]
    public async Task Request_UsesMethodPathParamsAndBaseUrl()
    {
        var (report, server) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              update:
                method: patch
                path: /users/${id}/profile
            steps:
              - call: update
                with: { id: 42 }
            """);

        Assert.True(report.Single().IsSuccess);
        var request = server.Requests.Single();
        Assert.Equal("PATCH", request.Method);
        Assert.Equal("https://api.test/v1/users/42/profile", request.Url);
        Assert.Equal("update id=42", report.Single().StepName);
    }

    [Fact]
    public async Task Query_SendsOnlyPassedParamsAndDefaults()
    {
        var (_, server) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              search:
                path: /users
                query: [q, page, active, size]
                params:
                  size: 20
            steps:
              - call: search
                with: { q: mario rossi & co, active: false }
            """);

        Assert.Equal("https://api.test/v1/users?q=mario%20rossi%20%26%20co&active=false&size=20", server.Requests.Single().Url);
    }

    [Fact]
    public async Task Headers_ApplyFileHeadersThenAuthSetsThenEndpointHeaders()
    {
        var (_, server) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              both:
                path: /both
                auth: [bearer, apikey]
                headers:
                  X-Client: override
            variables:
              token: abc
              apiKey: key-1
            steps:
              - call: both
            """);

        var headers = server.Requests.Single().Headers;
        Assert.Equal("Bearer abc", headers["Authorization"]);
        Assert.Equal("key-1", headers["X-API-Key"]);
        Assert.Equal("override", headers["X-Client"]);
    }

    [Fact]
    public async Task Body_IsJsonAndKeepsArgumentTypes()
    {
        var (_, server) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              create:
                method: POST
                path: /items
                body:
                  name: ${name}
                  active: ${active}
                  count: ${count}
                  tags: ${tags}
                  label: "item-${count}"
                  zip: "70100"
                  price: 9.90
                  note: null
            steps:
              - call: create
                with:
                  name: Ada
                  active: false
                  count: 3
                  tags: [a, b]
            """);

        var request = server.Requests.Single();
        Assert.StartsWith("application/json", request.ContentType);
        Assert.True(Tinyprobe.Core.Execution.JsonValues.AreEqual(
            Run.Json("""{"name":"Ada","active":false,"count":3,"tags":["a","b"],"label":"item-3","zip":"70100","price":9.9,"note":null}"""),
            Run.Json(request.Body!)));
    }

    [Fact]
    public async Task Body_AsStringIsSentAsText()
    {
        var (_, server) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              raw:
                method: POST
                path: /raw
                headers:
                  Content-Type: application/xml
                body: |
                  <user>${name}</user>
            steps:
              - call: raw
                with: { name: Ada }
            """);

        var request = server.Requests.Single();
        Assert.Equal("<user>Ada</user>\n", request.Body);
        Assert.Equal("application/xml", request.ContentType);
    }

    [Fact]
    public async Task NumericVariables_AreFormattedIndependentlyOfTheCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("it-IT");
        try
        {
            var (_, server) = await Run.Workflow($$"""
                {{Api}}
                endpoints:
                  items:
                    path: /items?max=${price}
                steps:
                  - call: items
                    with: { price: 9.9 }
                """);

            Assert.EndsWith("/items?max=9.9", server.Requests.Single().Url);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // --- Variables -------------------------------------------------------------

    [Fact]
    public async Task DeclaredParams_AreLocalAndUseTheirDefault()
    {
        var (_, server) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              login:
                method: POST
                path: /login
                params:
                  username: admin
                  password:
                body: { username: "${username}", password: "${password}" }
            variables:
              username: global-user        # ignored: declared params never read globals
            steps:
              - call: login
                with: { password: secret }
              - call: login
                with: { username: ada, password: other }
            """);

        Assert.Contains("\"username\":\"admin\"", server.Requests[0].Body);
        Assert.Contains("\"username\":\"ada\"", server.Requests[1].Body);
    }

    [Fact]
    public async Task SavedValues_AreUsedByLaterSteps_AndWithShadowsThemForOneStep()
    {
        var (report, server) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              login: { method: POST, path: /login }
              me: { path: /me, auth: bearer }
            steps:
              - call: login
                save: { token: $.access_token }
              - call: me
                with: { token: invalid-token }
              - call: me
            """,
            respond: r => r.Path.EndsWith("/login") ? new FakeResponse(Body: """{ "access_token": "t-123" }""") : new FakeResponse());

        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess));
        Assert.Equal("Bearer invalid-token", server.Requests[1].Headers["Authorization"]);
        Assert.Equal("Bearer t-123", server.Requests[2].Headers["Authorization"]);
    }

    [Fact]
    public async Task Environment_OverridesWorkflowVariables()
    {
        var (_, server) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              item: { path: "/${resource}/${id}" }
            variables:
              resource: users
              id: 1
            steps:
              - call: item
            """,
            environment: new() { ["id"] = "7" });

        Assert.EndsWith("/users/7", server.Requests.Single().Url);
    }

    [Fact]
    public async Task UndefinedVariable_FailsTheStepWithoutSending()
    {
        var (report, server) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              me: { path: /me, auth: bearer }
            steps:
              - call: me
            """);

        Assert.False(report.Single().IsSuccess);
        Assert.Equal("Variable 'token' is not defined.", report.Single().FailureReason);
        Assert.Null(report.Single().StatusCode);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task SaveOfAMissingValue_FailsTheStep()
    {
        var (report, _) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              login: { method: POST, path: /login }
            steps:
              - call: login
                save: { token: $.access_token }
            """);

        Assert.False(report.Single().IsSuccess);
        Assert.Contains("$.access_token not found", report.Single().FailureReason);
    }

    // --- Status ------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.OK, "", true)]
    [InlineData(HttpStatusCode.NoContent, "", true)]
    [InlineData(HttpStatusCode.NotFound, "", false)]           // without expect.status any 2xx is required
    [InlineData(HttpStatusCode.NotFound, "status: 404", true)]
    [InlineData(HttpStatusCode.OK, "status: 201", false)]
    [InlineData(HttpStatusCode.NoContent, "status: [200, 204]", true)]
    [InlineData(HttpStatusCode.NotFound, "status: [404, 410]", true)]
    [InlineData(HttpStatusCode.InternalServerError, "status: [200, 204]", false)]
    public async Task Status_IsChecked(HttpStatusCode actual, string expect, bool success)
    {
        var (report, _) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              item: { path: /item }
            steps:
              - call: item
                expect: { {{expect}} }
            """,
            respond: _ => new FakeResponse(actual));

        Assert.Equal(success, report.Single().IsSuccess);
        Assert.Equal(actual, report.Single().StatusCode);
    }

    // --- Body checks -------------------------------------------------------------

    private const string Payload = """
        {
          "id": 7,
          "name": "Ada",
          "price": 9.90,
          "active": true,
          "deletedAt": null,
          "tags": ["admin", "dev"],
          "user": { "address": { "city": "Bari" } },
          "items": [ { "id": 1 }, { "id": 2 } ],
          "matrix": [ [1, 2], [3, 4] ]
        }
        """;

    [Theory]
    [InlineData("$.name: Ada", true)]
    [InlineData("$.name: ada", false)]
    [InlineData("$.id: 7", true)]
    [InlineData("$.id: \"7\"", false)]                     // typed: the string "7" is not the number 7
    [InlineData("$.price: 9.9", true)]
    [InlineData("$.active: true", true)]
    [InlineData("$.deletedAt: null", true)]
    [InlineData("$.user.address.city: Bari", true)]
    [InlineData("$.items[1].id: 2", true)]
    [InlineData("$.matrix[1][0]: 3", true)]
    [InlineData("$.tags: [admin, dev]", true)]
    [InlineData("$.user: { eq: { address: { city: Bari } } }", true)]
    [InlineData("$.id: { gte: 7 }", true)]
    [InlineData("$.id: { gt: 7 }", false)]
    [InlineData("$.price: { gt: 0, lt: 10 }", true)]
    [InlineData("$.price: { gt: 0, lt: 5 }", false)]
    [InlineData("$.name: { eq: Ada, contains: d }", true)]     // equality plus another condition
    [InlineData("$.name: { eq: Ada, contains: z }", false)]
    [InlineData("$.name: { eq: Bob, contains: d }", false)]
    [InlineData("$.id: { exists: true, gt: 0, lte: 7 }", true)]
    [InlineData("$.name: { in: [Ada, Bob] }", true)]           // OR between values
    [InlineData("$.name: { in: [Bob, Eve] }", false)]
    [InlineData("$.id: { in: [6, 7] }", true)]
    [InlineData("$.id: { in: [\"7\"] }", false)]                // typed, like equality
    [InlineData("$.missing: { in: [1, 2] }", false)]
    [InlineData("$.id: { any: [ { lt: 0 }, { gt: 5 } ] }", true)]          // OR between conditions
    [InlineData("$.id: { any: [ { lt: 0 }, { gt: 100 } ] }", false)]
    [InlineData("$.id: { any: [ 1, { gte: 7, lt: 8 } ] }", true)]          // an alternative with an AND inside
    [InlineData("$.deletedAt: { any: [ null, { exists: false } ] }", true)]
    [InlineData("$.missing: { any: [ null, { exists: false } ] }", true)]
    [InlineData("$.name: { any: [ Bob, { contains: d } ], ne: Eve }", true)] // OR combined with AND
    [InlineData("$.name: { ne: Bob }", true)]
    [InlineData("$.name: { contains: d }", true)]
    [InlineData("$.tags: { contains: admin }", true)]
    [InlineData("$.tags: { contains: guest }", false)]
    [InlineData("$.deletedAt: { exists: true }", true)]   // null still exists
    [InlineData("$.missing: { exists: false }", true)]
    [InlineData("$.missing: { exists: true }", false)]
    [InlineData("$.missing: 1", false)]
    [InlineData("$.name: ${expected}", true)]
    public async Task BodyChecks(string check, bool passed)
    {
        var (report, _) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              item: { path: /item }
            variables:
              expected: Ada
            steps:
              - call: item
                expect:
                  body:
                    {{check}}
            """,
            respond: _ => new FakeResponse(Body: Payload));

        Assert.Equal(passed, report.Single().IsSuccess);
        Assert.Equal(passed, report.Single().Assertions.All(a => a.Passed));
    }

    [Fact]
    public async Task EveryCheckIsEvaluatedAndReported()
    {
        var (report, _) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              item: { path: /item }
            steps:
              - call: item
                expect:
                  body:
                    $.id: 7
                    $.name: Bob
                    $.tags: { contains: guest }
            """,
            respond: _ => new FakeResponse(Body: Payload));

        var step = report.Single();
        Assert.False(step.IsSuccess);
        Assert.Collection(step.Assertions,
            a => Assert.True(a.Passed),
            b => { Assert.False(b.Passed); Assert.Equal("$.name", b.Subject); Assert.Equal("== \"Bob\"", b.Condition); Assert.Equal("\"Ada\"", b.ActualValue); },
            c => { Assert.False(c.Passed); Assert.Equal("contains \"guest\"", c.Condition); });
    }

    [Fact]
    public async Task OrConditions_AreDescribedInTheReport()
    {
        var (report, _) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              item: { path: /item }
            steps:
              - call: item
                expect:
                  status: [200, 204]
                  body:
                    $.name: { in: [Bob, Eve] }
                    $.id: { any: [ { lt: 0 }, { gte: 100, lt: 200 }, null ] }
            """,
            respond: _ => new FakeResponse(Body: Payload));

        var step = report.Single();
        Assert.False(step.IsSuccess);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.NoContent], step.ExpectedStatusCodes);
        Assert.Collection(step.Assertions,
            a => Assert.Equal("in [\"Bob\",\"Eve\"]", a.Condition),
            b => Assert.Equal("< 0 or (>= 100 and < 200) or == null", b.Condition));
    }

    [Fact]
    public async Task UnexpectedStatus_ListsTheAcceptedCodes()
    {
        var (report, _) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              item: { path: /item }
            steps:
              - call: item
                expect: { status: [200, 204] }
            """,
            respond: _ => new FakeResponse(HttpStatusCode.InternalServerError));

        Assert.Equal("Expected status 200 or 204, got 500", report.Single().FailureReason);
    }

    [Fact]
    public async Task BodyChecksOnInvalidJson_FailTheStep()
    {
        var (report, _) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              page: { path: /page }
            steps:
              - call: page
                expect:
                  body:
                    $.title: Home
            """,
            respond: _ => new FakeResponse(Body: "<html></html>"));

        Assert.False(report.Single().IsSuccess);
        Assert.Equal("The response body is not valid JSON", report.Single().FailureReason);
    }

    // --- Header and duration checks ----------------------------------------------

    [Theory]
    [InlineData("x-request-id: abc", true)]                // header names are case-insensitive
    [InlineData("X-Request-Id: { contains: b }", true)]
    [InlineData("X-Request-Id: other", false)]
    [InlineData("X-Count: { gte: 2 }", true)]
    [InlineData("X-Count: 3", true)]                       // header values are compared as text
    [InlineData("X-Missing: { exists: false }", true)]
    [InlineData("X-Missing: { exists: true }", false)]
    [InlineData("X-Request-Id: { in: [abc, def] }", true)]
    [InlineData("X-Count: { in: [1, 2] }", false)]
    [InlineData("X-Request-Id: { any: [ { contains: z }, { contains: b } ] }", true)]
    public async Task HeaderChecks(string check, bool passed)
    {
        var (report, _) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              item: { path: /item }
            steps:
              - call: item
                expect:
                  headers:
                    {{check}}
            """,
            respond: _ => new FakeResponse { Headers = { ["X-Request-Id"] = "abc", ["X-Count"] = "3" } });

        Assert.Equal(passed, report.Single().IsSuccess);
    }

    [Theory]
    [InlineData("{ lt: 2s }", true)]
    [InlineData("{ gte: 50ms }", true)]
    [InlineData("{ lt: 10ms }", false)]
    public async Task DurationChecks(string check, bool passed)
    {
        var (report, _) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              slow: { path: /slow }
            steps:
              - call: slow
                expect:
                  duration: {{check}}
            """,
            respond: _ => new FakeResponse { Delay = TimeSpan.FromMilliseconds(80) });

        Assert.Equal(passed, report.Single().IsSuccess);
        Assert.Equal("duration", report.Single().Assertions.Single().Subject);
    }

    // --- Flow --------------------------------------------------------------------

    [Fact]
    public async Task FailingStep_StopsTheWorkflow()
    {
        var (report, server) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              item: { path: /item }
            steps:
              - call: item
                expect: { status: 201 }
              - call: item
            """);

        Assert.Single(report.StepReports);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Sleep_WaitsAndRaisesStepStarted()
    {
        var workflow = Tinyprobe.Core.Parsing.WorkflowLoader.Parse($$"""
            {{Api}}
            endpoints:
              item: { path: /item }
            steps:
              - sleep: 100ms
              - call: item
                name: After sleep
            """, "test.yaml");
        var engine = new Tinyprobe.Core.Execution.WorkflowEngine(new HttpClient(new FakeServer()));
        var started = new List<string>();
        var completed = new List<string>();
        engine.StepStarted += s => started.Add(s.Description);
        engine.StepCompleted += r => completed.Add(r.StepName);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await engine.ExecuteAsync(workflow);

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(80)); // OS timers may fire a few ms early
        Assert.Equal(["sleep 100ms", "After sleep"], started);
        Assert.Equal(["After sleep"], completed);
    }

    [Theory]
    [InlineData(2, 2, true, 3)]   // fails twice, 2 retries -> succeeds on the 3rd call
    [InlineData(2, 1, false, 2)]  // fails twice, 1 retry -> gives up after 2 calls
    [InlineData(1, 0, false, 1)]  // retries are off by default
    public async Task NetworkErrors_AreRetriedUpToMaxRetries(int failures, int maxRetries, bool success, int calls)
    {
        int attempt = 0;
        var (report, server) = await Run.Workflow($$"""
            {{Api}}
            settings: { max_retries: {{maxRetries}} }
            endpoints:
              ping: { path: /ping }
            steps:
              - call: ping
            """,
            respond: _ => new FakeResponse { NetworkError = ++attempt <= failures });

        Assert.Equal(success, report.Single().IsSuccess);
        Assert.Equal(calls, server.Requests.Count);
    }

    [Fact]
    public async Task HttpErrorStatus_IsNotRetried()
    {
        var (report, server) = await Run.Workflow($$"""
            {{Api}}
            settings: { max_retries: 3 }
            endpoints:
              ping: { path: /ping }
            steps:
              - call: ping
                expect: { status: 503 }
            """,
            respond: _ => new FakeResponse(HttpStatusCode.ServiceUnavailable));

        Assert.True(report.Single().IsSuccess);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Report_DescribesRequestAndResponse()
    {
        var (report, _) = await Run.Workflow($$"""
            {{Api}}
            endpoints:
              create:
                method: POST
                path: /users
                body: { name: ada }
            steps:
              - name: Create
                call: create
                expect: { status: 201 }
            """,
            respond: _ => new FakeResponse(HttpStatusCode.Created, """{ "id": 1 }"""));

        var step = report.Single();
        Assert.Equal("Create", step.StepName);
        Assert.Equal("POST", step.Method);
        Assert.Equal("https://api.test/v1/users", step.Url?.ToString());
        Assert.Equal(HttpStatusCode.Created, step.StatusCode);
        Assert.Equal([HttpStatusCode.Created], step.ExpectedStatusCodes);
        Assert.Equal(14, step.BytesSent);       // {"name":"ada"}
        Assert.Equal(11, step.BytesReceived);
        Assert.True(step.Duration > TimeSpan.Zero);
    }
}
