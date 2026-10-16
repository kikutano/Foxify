using System.Text.Json;
using Foxify.Core.Definitions;
using Foxify.Core.Parsing;

namespace Foxify.Core.Tests.Parsing;

public sealed class WorkflowLoaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("foxify-loader-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static WorkflowLoadException LoadFails(string yaml) =>
        Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Parse(yaml, "test.yaml"));

    private const string Endpoints = """
        endpoints:
          login:
            method: POST
            path: /login
            params:
              username: admin
              password:
          get_user:
            path: /users/${id}
        """;

    [Fact]
    public void Imports_AreResolvedRelativeToTheWorkflow()
    {
        Write("api/server.yaml", """
            base_url: ${baseUrl}
            headers:
              X-Client: foxify
            auth:
              bearer:
                Authorization: "Bearer ${token}"
            endpoints:
              me:
                path: /me
                auth: bearer
            """);
        var workflowPath = Write("workflows/me.yaml", """
            name: Me
            import: [../api/server.yaml]
            steps:
              - call: me
            """);

        var workflow = WorkflowLoader.Load(workflowPath);

        Assert.Equal("Me", workflow.Name);
        var endpoint = Assert.IsType<CallStep>(workflow.Steps.Single()).Endpoint;
        Assert.Equal("me", endpoint.Name);
        Assert.Equal("${baseUrl}", endpoint.BaseUrl?.ToString());
        Assert.Equal(["X-Client", "Authorization"], endpoint.Headers.Select(h => h.Key));
    }

    [Fact]
    public void DuplicateEndpoints_AcrossFiles_AreAnError()
    {
        Write("a.yaml", "endpoints:\n  login: { path: /a }\n");
        Write("b.yaml", "endpoints:\n  login: { path: /b }\n");
        var workflowPath = Write("w.yaml", "import: [a.yaml, b.yaml]\nsteps:\n  - call: login\n");

        var error = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Load(workflowPath)).Errors.Single();

        Assert.Contains("b.yaml:2:3: endpoint 'login' is already defined at", error);
        Assert.Contains("a.yaml:2:3", error);
    }

    [Fact]
    public void MissingImport_IsAnError()
    {
        var workflowPath = Write("w.yaml", "import: [nope.yaml]\nsteps:\n  - sleep: 1s\n");

        var error = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Load(workflowPath)).Errors.Single();

        Assert.EndsWith("w.yaml:1:10: imported file not found: nope.yaml", error);
    }

    [Theory]
    [InlineData("  - call: logn", "unknown endpoint 'logn' (did you mean 'login'?)")]
    [InlineData("  - call: login", "missing required param 'password' of endpoint 'login'")]
    [InlineData("  - call: login\n    expcet: { status: 200 }", "unknown key 'expcet' in a step (did you mean 'expect'?)")]
    [InlineData("  - call: get_user\n    expect: { status: 700 }", "invalid status code '700'")]
    [InlineData("  - call: get_user\n    expect: { body: { $.a..b: 1 } }", "invalid JSONPath '$.a..b'")]
    [InlineData("  - call: get_user\n    save: { id: data.id }", "invalid JSONPath 'data.id'")]
    [InlineData("  - call: get_user\n    expect: { body: { $.a: { gte: 1, name: x } } }", "cannot mix operators")]
    [InlineData("  - call: get_user\n    expect: { body: { $.a: { exists: maybe } } }", "exists must be true or false")]
    [InlineData("  - call: get_user\n    expect: { status: [] }", "status needs at least one code")]
    [InlineData("  - call: get_user\n    expect: { status: [200, abc] }", "invalid status code 'abc'")]
    [InlineData("  - call: get_user\n    expect: { body: { $.a: { in: x } } }", "in needs a list of values")]
    [InlineData("  - call: get_user\n    expect: { body: { $.a: { in: [] } } }", "in needs a list of values")]
    [InlineData("  - call: get_user\n    expect: { body: { $.a: { any: { lt: 1 } } } }", "any needs a list of conditions")]
    [InlineData("  - call: get_user\n    expect: { body: { $.a: { any: [ { lt: 1, x: 2 } ] } } }", "cannot mix operators")]
    [InlineData("  - call: get_user\n    with: { id: 1 }\n    until: { every: 1s }", "until needs a condition: status, headers or body")]
    [InlineData("  - call: get_user\n    with: { id: 1 }\n    until: { status: 200, every: soon }", "invalid duration 'soon'")]
    [InlineData("  - call: get_user\n    with: { id: 1 }\n    until: { status: 200, every: 0ms }", "every must be greater than 0")]
    [InlineData("  - call: get_user\n    with: { id: 1 }\n    until: { status: 200, every: 5s, timeout: 1s }", "timeout (1s) must be at least every (5s)")]
    [InlineData("  - call: get_user\n    with: { id: 1 }\n    until: { duration: { lt: 1s } }", "unknown key 'duration' in until")]
    [InlineData("  - call: get_user\n    with: { id: 1 }\n    until: { body: { $.a..b: 1 } }", "invalid JSONPath '$.a..b'")]
    [InlineData("  - sleep: 1s\n    until: { status: 200 }", "a sleep step cannot have 'until'")]
    [InlineData("  - call: get_user\n    with: { id: \"${$uid}\" }", "unknown generated value '$uid' (did you mean '$uuid'?)")]
    [InlineData("  - call: get_user\n    with: { id: \"${$now+2x}\" }", "invalid offset '2x' in '$now+2x'")]
    [InlineData("  - call: get_user\n    expect: { duration: 2s }", "duration needs a comparison")]
    [InlineData("  - call: get_user\n    expect: { duration: { lt: soon } }", "invalid duration 'soon'")]
    [InlineData("  - call: get_user\n    expect: { duration: { eq: 1s } }", "unknown key 'eq' in expect.duration")]
    [InlineData("  - sleep: soon", "invalid duration 'soon'")]
    [InlineData("  - sleep: 1s\n    call: login", "a sleep step cannot have 'call'")]
    [InlineData("  - name: nothing", "a step needs 'call' (or 'sleep')")]
    public void InvalidSteps_AreReportedWithTheirLocation(string step, string message)
    {
        var errors = LoadFails($"{Endpoints}\nsteps:\n{step}\n").Errors;

        Assert.Contains(errors, e => e.Contains(message) && e.StartsWith("test.yaml:"));
    }

    [Theory]
    [InlineData("endpoints:\n  x: { path: /x, auth: berer }\nauth:\n  bearer: { A: b }", "unknown auth 'berer' (did you mean 'bearer'?)")]
    [InlineData("endpoints:\n  x: { method: 'GET /', path: /x }", "invalid HTTP method 'GET /'")]
    [InlineData("endpoints:\n  x: { method: GET }", "endpoint 'x' has no path")]
    [InlineData("endpoints:\n  x: { path: /x, query: page }", "query must be a list")]
    [InlineData("settings: { timeout: 0 }", "timeout must be a positive number of seconds")]
    [InlineData("stesp: []", "unknown key 'stesp' in workflow (did you mean 'steps'?)")]
    public void InvalidDefinitions_AreReported(string yaml, string message)
    {
        var errors = LoadFails($"{yaml}\nsteps:\n  - sleep: 1s\n").Errors;

        Assert.Contains(errors, e => e.Contains(message));
    }

    private const string ArgumentsApi = """
        base_url: ${baseUrl}
        auth:
          bearer: { Authorization: "Bearer ${token}" }
        endpoints:
          list_users:
            path: /users
            query: [page, pageSize]
            auth: bearer
          get_user:
            path: /users/${id}
            auth: bearer
          create_user:
            method: POST
            path: /users
            params: { email: }
            body: { email: "${email}", meta: "${meta}" }
        """;

    [Theory]
    [InlineData("call: list_users\n    with: { pageSize: 2 }")]                       // query name
    [InlineData("call: get_user\n    with: { id: 1, token: invalid-token }")]          // placeholder of an auth set
    [InlineData("call: get_user\n    with: { id: 1, baseUrl: http://other }")]         // placeholder of base_url
    [InlineData("call: create_user\n    with: { email: a@b.c, meta: x }")]             // param and body placeholder
    [InlineData("call: get_user\n    with: { id: 1, expected: Ada }\n    expect: { body: { $.name: { any: [ \"${expected}\" ] } } }")] // used by a check
    [InlineData("call: get_user\n    with: { id: 1, wanted: Ada }\n    until: { body: { $.name: \"${wanted}\" } }")]  // used by an until check
    public void KnownArguments_AreAccepted(string step)
    {
        WorkflowLoader.Parse($"{ArgumentsApi}\nsteps:\n  - {step}\n", "test.yaml");
    }

    [Fact]
    public void UnknownArguments_AreReported_WithASuggestion()
    {
        var errors = LoadFails($"{ArgumentsApi}\nsteps:\n  - call: list_users\n    with: {{ pagesize: 2, foo: bar }}\n").Errors;

        Assert.Equal(2, errors.Count);
        Assert.Contains("unknown argument 'pagesize' for endpoint 'list_users' (did you mean 'pageSize'?)", errors[0]);
        Assert.Contains("unknown argument 'foo' for endpoint 'list_users'", errors[1]);
    }

    [Fact]
    public void AllProblems_AreReportedTogether_InFileOrder()
    {
        var errors = LoadFails($$"""
            {{Endpoints}}
            steps:
              - call: logn
              - sleep: soon
              - call: get_user
                expect: { status: 999 }
            """).Errors;

        Assert.Equal(3, errors.Count);
        Assert.Contains("unknown endpoint", errors[0]);
        Assert.Contains("invalid duration", errors[1]);
        Assert.Contains("invalid status code", errors[2]);
    }

    [Fact]
    public void YamlSyntaxErrors_HaveALocation()
    {
        var error = LoadFails("steps:\n  - call: [unclosed\n").Errors.Single();

        Assert.Matches(@"^test\.yaml:\d+:\d+: ", error);
    }

    [Fact]
    public void WorkflowWithoutSteps_IsAnError()
    {
        Assert.Contains("the workflow has no steps", LoadFails(Endpoints).Errors.Single());
    }

    [Fact]
    public void ApiFiles_CannotContainSteps()
    {
        Write("api.yaml", "endpoints:\n  x: { path: /x }\nsteps: []\n");
        var workflowPath = Write("w.yaml", "import: [api.yaml]\nsteps:\n  - call: x\n");

        var error = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Load(workflowPath)).Errors.Single();

        Assert.Contains("unknown key 'steps' in an API file", error);
    }

    [Fact]
    public void DefaultStepName_ShowsPathAndQueryArgumentsButNotTheOthers()
    {
        var workflow = WorkflowLoader.Parse("""
            endpoints:
              login:
                method: POST
                path: /tenants/${tenant}/login
                query: [lang]
                body: { password: "${password}" }
              ping: { path: /ping }
            steps:
              - call: login
                with: { tenant: acme, lang: it, password: s3cret }
              - call: ping
              - call: ping
                name: Custom name
            """, "test.yaml");

        Assert.Equal(["login tenant=acme lang=it", "ping", "Custom name"], workflow.Steps.Cast<CallStep>().Select(s => s.Name));
    }

    [Fact]
    public void Expectations_AreParsed()
    {
        var step = (CallStep)WorkflowLoader.Parse($$"""
            {{Endpoints}}
            steps:
              - call: get_user
                with: { id: 1 }
                expect:
                  status: 200
                  headers:
                    Content-Type: { contains: json }
                  body:
                    $.email: ada@example.com
                    $.total: { gte: 1, lt: 10 }
                    $.deleted: { exists: false }
                  duration: { lt: 500ms }
                save:
                  userId: $.id
            """, "test.yaml").Steps.Single();

        Assert.Equal([200], step.Expect.Status);
        Assert.Equal(Operator.Contains, step.Expect.Headers.Single().Conditions.Single().Operator);
        Assert.Equal(["$.email", "$.total", "$.deleted"], step.Expect.Body.Select(c => c.Subject));
        Assert.Equal([Operator.GreaterOrEqual, Operator.LessThan], step.Expect.Body[1].Conditions.Select(c => c.Operator));
        Assert.False(step.Expect.Body[2].Conditions.Single().Exists);
        Assert.Equal(TimeSpan.FromMilliseconds(500), step.Expect.Duration.Single().DurationValue);
        Assert.Equal("$.id", step.Save["userId"]);
    }

    [Fact]
    public void Environment_KeepsYamlTypes()
    {
        var path = Write("env.yaml", """
            baseUrl: "http://localhost:5122/"
            port: 8080
            debug: true
            zip: "70100"
            """);

        var environment = WorkflowLoader.LoadEnvironment(path);

        Assert.Equal(JsonValueKind.String, environment["baseUrl"].ValueKind);
        Assert.Equal(8080, environment["port"].GetInt32());
        Assert.Equal(JsonValueKind.True, environment["debug"].ValueKind);
        Assert.Equal("70100", environment["zip"].GetString());
    }
}
