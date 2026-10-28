using System.Net;
using System.Text;
using System.Text.Json;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Models;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Protocol;
using Tinyprobe.Core.Tests.Fakes;

namespace Tinyprobe.Core.Tests.Execution;

/// <summary><c>basic</c> and <c>oauth2</c> in auth sets (docs/specs/0010-basic-and-oauth2.md).</summary>
public sealed class AuthSchemeTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private const string Api = """
        base_url: https://api.test
        auth:
          admin:
            basic:
              username: ${username}
              password: ${password}
          service:
            oauth2:
              token_url: https://auth.test/token
              client_id: my-client
              client_secret: ${secret}
              scope: [orders:read, orders:write]
            X-Tenant: acme
          service_form:
            oauth2:
              token_url: https://auth.test/token
              client_id: my-client
              client_secret: ${secret}
              scope: orders:read
              client_auth: form
              form: { audience: "https://api.test" }
        endpoints:
          stats:
            path: /admin/stats
            auth: admin
            params: { username: admin, password: }
          orders:
            path: /orders
            auth: service
          order:
            path: /orders/1
            auth: service
          orders_form:
            path: /orders
            auth: service_form
        """;

    private static string Basic(string credentials) => Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));

    private static int _tokens;

    /// <summary>The token URL issues tok-1, tok-2... with <paramref name="expiresIn"/>; the API answers 200.</summary>
    private static Func<CapturedRequest, FakeResponse> Server(int expiresIn = 3600) => request =>
        request.Url.StartsWith("https://auth.test/", StringComparison.Ordinal)
            ? new FakeResponse(Body: $$"""{"access_token":"tok-{{Interlocked.Increment(ref _tokens)}}","token_type":"bearer","expires_in":{{expiresIn}}}""")
            : new FakeResponse();

    private static async Task<(WorkflowReport Report, FakeServer Server)> RunAsync(
        string steps, Func<CapturedRequest, FakeResponse>? respond = null, ManualTime? time = null, string settings = "")
    {
        var workflow = WorkflowLoader.Parse($"{Api}\n{settings}\nsteps:\n{steps}", "test.yaml");
        var server = new FakeServer(respond ?? Server());
        var engine = new WorkflowEngine(new HttpClient(server)) { RecordExchanges = true, Time = time ?? new ManualTime() };
        var environment = new Dictionary<string, JsonElement> { ["secret"] = JsonValues.FromString("s3cret") };
        return (await engine.ExecuteAsync(workflow, environment), server);
    }

    private static void AllPass(WorkflowReport report) =>
        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess, s.FailureReason));

    // --- basic -----------------------------------------------------------------

    [Fact]
    public async Task Basic_SendsTheBase64OfUsernameAndPassword_AsUtf8()
    {
        var (report, server) = await RunAsync("""
              - call: stats
                with: { password: "pässword" }
              - call: stats
                with: { username: ops, password: x }
            """);

        AllPass(report);
        Assert.Equal($"Basic {Basic("admin:pässword")}", server.Requests[0].Headers["Authorization"]);
        Assert.Equal("Basic b3BzOng=", server.Requests[1].Headers["Authorization"]);
        Assert.Equal("Basic b3BzOng=", report.StepReports[1].Request!.Headers.Single(h => h.Key == "Authorization").Value);
    }

    [Fact]
    public async Task Basic_WithAColonInTheUsername_FailsTheStep()
    {
        var (report, server) = await RunAsync("""
              - call: stats
                with: { username: "a:b", password: x }
            """);

        Assert.Equal("Basic auth: the username cannot contain ':'", report.Single().FailureReason);
        Assert.Empty(server.Requests);
    }

    // --- oauth2 ----------------------------------------------------------------

    [Fact]
    public async Task OAuth2_GetsAToken_WithTheClientInABasicHeader_ThenSendsItAsBearer()
    {
        var (report, server) = await RunAsync("  - call: orders\n");

        AllPass(report);
        var (token, api) = (server.Requests[0], server.Requests[1]);
        Assert.Equal(("POST", "https://auth.test/token"), (token.Method, token.Url));
        Assert.Equal($"Basic {Basic("my-client:s3cret")}", token.Headers["Authorization"]);
        Assert.Equal("application/x-www-form-urlencoded", token.ContentType);
        Assert.Equal("grant_type=client_credentials&scope=orders%3Aread+orders%3Awrite", token.Body);
        Assert.StartsWith("Bearer tok-", api.Headers["Authorization"]);
        Assert.Equal("acme", api.Headers["X-Tenant"]);
        Assert.Equal(api.Headers["Authorization"], report.Single().Request!.Headers.Single(h => h.Key == "Authorization").Value);
    }

    [Fact]
    public async Task OAuth2_WithClientAuthForm_SendsTheClientAndTheExtraFieldsInTheBody()
    {
        var (report, server) = await RunAsync("  - call: orders_form\n");

        AllPass(report);
        var token = server.Requests[0];
        Assert.False(token.Headers.ContainsKey("Authorization"));
        Assert.Equal(
            "grant_type=client_credentials&scope=orders%3Aread&client_id=my-client&client_secret=s3cret&audience=https%3A%2F%2Fapi.test",
            token.Body);
    }

    [Fact]
    public async Task OAuth2_ReusesTheToken_UntilItExpires()
    {
        var time = new ManualTime();
        var (report, server) = await RunAsync("""
              - call: orders
              - call: order
              - sleep: 1ms
              - call: orders
            """, Server(expiresIn: 60), time);

        AllPass(report);
        Assert.Equal(1, server.Requests.Count(r => r.Url.StartsWith("https://auth.test/")));
        Assert.Equal(server.Requests[1].Headers["Authorization"], server.Requests[3].Headers["Authorization"]);
    }

    [Fact]
    public async Task OAuth2_GetsANewToken_ThirtySecondsBeforeExpiry()
    {
        var time = new ManualTime();
        var server = new FakeServer(Server(expiresIn: 60));
        var engine = new WorkflowEngine(new HttpClient(server)) { Time = time };
        var environment = new Dictionary<string, JsonElement> { ["secret"] = JsonValues.FromString("s3cret") };
        engine.StepStarted += progress => time.Now += TimeSpan.FromSeconds(progress.Index == 2 ? 31 : 0);

        var report = await engine.ExecuteAsync(WorkflowLoader.Parse($"{Api}\nsteps:\n  - call: orders\n  - call: orders\n  - call: orders\n", "test.yaml"), environment);

        AllPass(report);
        var bearers = server.Requests.Where(r => r.Path == "/orders").Select(r => r.Headers["Authorization"]).ToList();
        Assert.Equal(bearers[0], bearers[1]);
        Assert.NotEqual(bearers[1], bearers[2]);
        Assert.Equal(2, server.Requests.Count(r => r.Path == "/token"));
    }

    [Fact]
    public async Task OAuth2_SetsWithDifferentClients_GetTheirOwnTokens_AndEachRunStartsWithNone()
    {
        var (first, firstServer) = await RunAsync("  - call: orders\n  - call: orders_form\n");
        var (second, secondServer) = await RunAsync("  - call: orders\n");

        AllPass(first);
        AllPass(second);
        Assert.Equal(2, firstServer.Requests.Count(r => r.Path == "/token"));
        Assert.Equal(1, secondServer.Requests.Count(r => r.Path == "/token"));
    }

    [Theory]
    [InlineData(401, """{"error":"invalid_client"}""", """Cannot get an OAuth2 token from https://auth.test/token: status 401, {"error":"invalid_client"}""")]
    [InlineData(200, """{"token_type":"bearer"}""", "Cannot get an OAuth2 token from https://auth.test/token: the response has no access_token")]
    [InlineData(200, "<html></html>", "Cannot get an OAuth2 token from https://auth.test/token: the response is not JSON: <html></html>")]
    public async Task OAuth2_AFailingTokenRequest_FailsTheStep_WithoutCallingTheApi(int status, string body, string expected)
    {
        var (report, server) = await RunAsync("  - call: orders\n", _ => new FakeResponse((HttpStatusCode)status, body));

        Assert.Equal(expected, report.Single().FailureReason);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task OAuth2_ANetworkErrorOfTheTokenRequest_IsRetried()
    {
        int calls = 0;
        var tokens = Server();
        var (report, server) = await RunAsync(
            "  - call: orders\n",
            request => request.Path == "/token" && ++calls == 1 ? new FakeResponse { NetworkError = true } : tokens(request),
            settings: "settings: { max_retries: 1 }");

        AllPass(report);
        Assert.Equal(2, server.Requests.Count(r => r.Path == "/token"));
    }

    [Fact]
    public async Task OAuth2_ANetworkErrorWithoutRetries_SaysWhichRequestFailed()
    {
        var (report, _) = await RunAsync("  - call: orders\n", _ => new FakeResponse { NetworkError = true });

        Assert.Equal("Cannot get an OAuth2 token from https://auth.test/token: Connection refused.", report.Single().FailureReason);
    }

    // --- validation ------------------------------------------------------------

    [Theory]
    [InlineData("  a:\n    basic: { username: x }", "test.yaml:3:5: auth 'a': basic needs username and password")]
    [InlineData("  a:\n    basic: { usrname: x, password: y }", "test.yaml:3:14: unknown key 'usrname' in basic (did you mean 'username'?)")]
    [InlineData("  a:\n    oauth2: { token_url: x, client_id: y }", "test.yaml:3:5: auth 'a': oauth2 needs token_url, client_id and client_secret")]
    [InlineData("  a:\n    oauth2: { token_uri: x, token_url: x, client_id: y, client_secret: z }", "test.yaml:3:15: unknown key 'token_uri' in oauth2 (did you mean 'token_url'?)")]
    [InlineData("  a:\n    oauth2: { token_url: x, client_id: y, client_secret: z, client_auth: header }", "test.yaml:3:74: client_auth must be basic or form, not 'header'")]
    [InlineData("  a:\n    oauth2: { token_url: x, client_id: y, client_secret: z, scope: { a: b } }", "test.yaml:3:68: scope must be a value or a list of values")]
    [InlineData("  a:\n    basic: { username: x, password: y }\n    Authorization: z", "test.yaml:4:20: auth 'a' has basic and an Authorization header: use one of them")]
    [InlineData("  a:\n    basic: { username: x, password: y }\n    oauth2: { token_url: x, client_id: y, client_secret: z }", "test.yaml:3:5: auth 'a' has basic and oauth2: use one of them")]
    public void InvalidAuthSets_AreReportedWithTheirLocation(string sets, string expected)
    {
        var errors = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Parse(
            $"auth:\n{sets}\nendpoints:\n  x: {{ path: /x }}\nsteps:\n  - call: x\n", "test.yaml")).Errors;

        Assert.Contains(expected, errors);
    }

    [Theory]
    [InlineData("auth: [admin, bearer]", "test.yaml:8:11: endpoint 'x' uses auth 'admin' and 'bearer', which both set Authorization: use one of them")]
    [InlineData("auth: [admin, service]", "test.yaml:8:11: endpoint 'x' uses auth 'admin' and 'service', which both set Authorization: use one of them")]
    [InlineData("auth: admin\n    headers: { authorization: x }", "test.yaml:9:31: endpoint 'x' sets authorization, but its auth 'admin' sets its own: remove the header")]
    public void AnotherAuthorizationHeader_NextToAScheme_IsAnError(string endpoint, string expected)
    {
        var errors = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Parse($$"""
            auth:
              admin: { basic: { username: a, password: b } }
              service: { oauth2: { token_url: u, client_id: i, client_secret: s } }
              bearer: { Authorization: "Bearer t" }
            endpoints:
              x:
                path: /x
                {{endpoint}}
            steps:
              - call: x
            """, "test.yaml")).Errors;

        Assert.Contains(expected, errors);
    }

    [Fact]
    public async Task AnAuthorizationHeaderOfTheFile_IsNotSent_NextToAScheme()
    {
        var (report, server) = await Run.Workflow("""
            base_url: https://api.test
            headers: { Authorization: "Bearer file-token" }
            auth:
              admin: { basic: { username: a, password: b } }
            endpoints:
              x: { path: /x, auth: admin }
            steps:
              - call: x
            """);

        Assert.True(report.Single().IsSuccess, report.Single().FailureReason);
        Assert.Equal($"Basic {Basic("a:b")}", server.Requests.Single().Headers["Authorization"]);
    }

    [Fact]
    public void Placeholders_OfSchemes_AreArguments()
    {
        var errors = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Parse(
            $"{Api}\nsteps:\n  - call: stats\n    with: {{ pasword: x }}\n", "test.yaml")).Errors;

        Assert.Contains(errors, e => e.EndsWith("unknown argument 'pasword' for endpoint 'stats' (did you mean 'password'?)"));
    }

    [Fact]
    public void Inspection_ShowsTheSchemes_AndTheirArguments()
    {
        var json = JsonDocument.Parse(ProtocolJson.Inspection("1.2.3", WorkflowLoader.Parse(
            $"{Api}\nsteps:\n  - call: orders\n", "test.yaml"), indented: false)).RootElement;

        var endpoints = json.GetProperty("endpoints").EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!);
        Assert.Equal("""{"username":"${username}","password":"${password}"}""", endpoints["stats"].GetProperty("basic_auth").GetRawText());
        Assert.Equal(JsonValueKind.Null, endpoints["stats"].GetProperty("oauth2").ValueKind);
        Assert.Equal(
            """{"token_url":"https://auth.test/token","client_id":"my-client","client_secret":"${secret}","scope":"orders:read orders:write","client_auth":"basic","form":{}}""",
            endpoints["orders"].GetProperty("oauth2").GetRawText());
        Assert.Equal("""{"audience":"https://api.test"}""", endpoints["orders_form"].GetProperty("oauth2").GetProperty("form").GetRawText());
        Assert.Equal("""[{"name":"X-Tenant","value":"acme"}]""", endpoints["orders"].GetProperty("headers").GetRawText());
        Assert.Equal("""["secret"]""", endpoints["orders"].GetProperty("arguments").GetRawText());
        Assert.Equal("""["password","username"]""", endpoints["stats"].GetProperty("arguments").GetRawText());
    }
}
