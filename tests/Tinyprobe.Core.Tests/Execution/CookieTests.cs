using System.Net;
using System.Text.Json;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Protocol;
using Tinyprobe.Core.Tests.Fakes;

namespace Tinyprobe.Core.Tests.Execution;

/// <summary>The cookie jar, <c>expect.cookies</c> and <c>save</c> from a cookie (docs/specs/0009-cookies.md).</summary>
public sealed class CookieTests
{
    private const string Api = """
        base_url: https://api.test
        endpoints:
          login: { method: POST, path: /session }
          me: { path: /session/me }
          admin: { path: /admin/stats }
          manual:
            path: /session/me
            headers: { Cookie: "lang=it" }
          logout: { method: DELETE, path: /session }
          note:
            method: POST
            path: /notes
            params: { csrf: }
            headers: { X-XSRF-TOKEN: "${csrf}" }
        """;

    /// <summary>The login sets a session for the whole site, a CSRF token, and a cookie for /admin only; logout removes the session.</summary>
    private static FakeResponse Respond(CapturedRequest request) => (request.Method, request.Path) switch
    {
        ("POST", "/session") => new FakeResponse
        {
            SetCookies = ["sid=abc123; Path=/; HttpOnly", "XSRF-TOKEN=tok-1; Path=/", "area=admin; Path=/admin"]
        },
        ("DELETE", "/session") => new FakeResponse { SetCookies = ["sid=; Path=/; Max-Age=0"] },
        _ => new FakeResponse(Body: $$"""{"cookie":{{JsonSerializer.Serialize(request.Headers.GetValueOrDefault("Cookie"))}}}""")
    };

    private static Task<(Models.WorkflowReport Report, FakeServer Server)> RunAsync(string steps) =>
        Run.Workflow($"{Api}\nsteps:\n{steps}", Respond);

    [Fact]
    public async Task Cookies_SetByOneStep_AreSentByTheNextOnes_WhenTheirPathMatches()
    {
        var (report, server) = await RunAsync("""
              - call: login
              - call: me
              - call: admin
            """);

        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess, s.FailureReason));
        Assert.False(server.Requests[0].Headers.ContainsKey("Cookie"));
        Assert.Equal("sid=abc123; XSRF-TOKEN=tok-1", server.Requests[1].Headers["Cookie"]);
        Assert.Equal("area=admin; sid=abc123; XSRF-TOKEN=tok-1", server.Requests[2].Headers["Cookie"]);
    }

    [Fact]
    public async Task ExpectCookies_ReadsTheJarAfterTheResponse()
    {
        var (report, _) = await RunAsync("""
              - call: login
                expect:
                  cookies:
                    sid: { exists: true }
                    XSRF-TOKEN: tok-1
                    xsrf-token: { exists: false }
              - call: me
                expect:
                  cookies:
                    sid: abc123
                    area: { exists: false }
              - call: logout
                expect:
                  cookies:
                    sid: { exists: false }
                    XSRF-TOKEN: { exists: true }
            """);

        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess, s.FailureReason));
        Assert.Equal(
            ["cookie sid exists", "cookie XSRF-TOKEN == \"tok-1\"", "cookie xsrf-token does not exist"],
            report.StepReports[0].Assertions.Select(a => $"{a.Subject} {a.Condition}"));
        Assert.Equal("\"abc123\"", report.StepReports[1].Assertions[0].ActualValue);
    }

    [Fact]
    public async Task AFailingCookieCheck_ShowsTheValue_OrMissing()
    {
        var (report, _) = await RunAsync("""
              - call: login
                expect:
                  cookies:
                    sid: other
                    theme: dark
            """);

        var step = report.Single();
        Assert.False(step.IsSuccess);
        Assert.Equal(["\"abc123\"", "(missing)"], step.Assertions.Select(a => a.ActualValue));
        Assert.All(step.Assertions, a => Assert.False(a.Passed));
    }

    [Fact]
    public async Task Save_ReadsACookie_ForTheNextSteps()
    {
        var (report, server) = await RunAsync("""
              - call: login
                save:
                  csrf: { cookie: XSRF-TOKEN }
              - call: note
                with: { csrf: "${csrf}" }
            """);

        Assert.All(report.StepReports, s => Assert.True(s.IsSuccess, s.FailureReason));
        Assert.Equal("tok-1", report.StepReports[0].Saved.Single().Value.GetString());
        Assert.Equal("tok-1", server.Requests[1].Headers["X-XSRF-TOKEN"]);
    }

    [Fact]
    public async Task Save_OfACookieThatIsNotInTheJar_FailsTheStep()
    {
        var (report, _) = await RunAsync("""
              - call: me
                save:
                  csrf: { cookie: XSRF-TOKEN }
            """);

        Assert.Equal("Cannot save 'csrf': no cookie 'XSRF-TOKEN' after this response", report.Single().FailureReason);
    }

    [Fact]
    public async Task Save_FromACookieOnly_DoesNotNeedAJsonBody()
    {
        var (report, _) = await Run.Workflow("""
            base_url: https://api.test
            endpoints:
              page: { path: /login }
            steps:
              - call: page
                save:
                  csrf: { cookie: csrftoken }
            """, _ => new FakeResponse(Body: "<html></html>") { SetCookies = ["csrftoken=t9; Path=/"] });

        Assert.True(report.Single().IsSuccess, report.Single().FailureReason);
    }

    [Fact]
    public async Task Until_WaitsForACookie()
    {
        int calls = 0;
        var (report, _) = await Run.Workflow("""
            base_url: https://api.test
            endpoints:
              export: { path: /export }
            steps:
              - call: export
                until:
                  cookies: { export_ready: { exists: true } }
                  every: 10ms
                  timeout: 1s
            """, _ => ++calls < 3 ? new FakeResponse() : new FakeResponse { SetCookies = ["export_ready=1"] });

        Assert.True(report.Single().IsSuccess, report.Single().FailureReason);
        Assert.Equal(3, report.Single().Attempts);
    }

    [Fact]
    public async Task TheRecordedRequest_HasTheCookieHeaderAsSent()
    {
        var cookies = new CookieContainer();
        var server = new FakeServer(Respond) { Cookies = cookies };
        var engine = new WorkflowEngine(new HttpClient(server), cookies: cookies) { RecordExchanges = true };

        var report = await engine.ExecuteAsync(WorkflowLoader.Parse($"""
            {Api}
            steps:
              - call: login
              - call: manual
            """, "test.yaml"));

        Assert.DoesNotContain(report.StepReports[0].Request!.Headers, h => h.Key == "Cookie");
        var sent = report.StepReports[1].Request!.Headers.Single(h => h.Key == "Cookie").Value;
        Assert.Equal("lang=it; sid=abc123; XSRF-TOKEN=tok-1", sent);
        Assert.Equal(sent, server.Requests[1].Headers["Cookie"]);
    }

    [Fact]
    public async Task EachRun_StartsWithAnEmptyJar()
    {
        const string login = "  - call: login\n";
        const string checkEmpty = "  - call: me\n    expect: { cookies: { sid: { exists: false } } }\n";

        var (first, _) = await RunAsync(login);
        var (second, server) = await RunAsync(checkEmpty);

        Assert.True(first.Single().IsSuccess);
        Assert.True(second.Single().IsSuccess, second.Single().FailureReason);
        Assert.False(server.Requests.Single().Headers.ContainsKey("Cookie"));
    }

    // --- validation ------------------------------------------------------------

    [Theory]
    [InlineData("    expect:\n      cookie: { sid: x }", "test.yaml:7:7: unknown key 'cookie' in expect (did you mean 'cookies'?)")]
    [InlineData("    save:\n      csrf: { cooky: XSRF-TOKEN }", "test.yaml:7:15: unknown key 'cooky' in save 'csrf' (did you mean 'cookie'?)")]
    [InlineData("    save:\n      csrf: [a]", "test.yaml:7:13: save 'csrf' must be a JSONPath ($.token) or { cookie: name }")]
    [InlineData("    save:\n      csrf: {}", "test.yaml:7:13: save 'csrf' must be a JSONPath ($.token) or { cookie: name }")]
    [InlineData("    save:\n      csrf: { cookie: }", "test.yaml:7:23: save 'csrf' needs the name of a cookie: { cookie: name }")]
    [InlineData("    expect:\n      cookies: { sid: { exists: true, a: 1 } }", "test.yaml:7:23: cannot mix operators (gte, exists, ...) with object fields; to compare an object use eq: { ... }")]
    [InlineData("    until:\n      every: 1s", "test.yaml:7:7: until needs a condition: status, headers, cookies or body")]
    public void InvalidCookieKeys_AreReportedWithTheirLocation(string step, string expected)
    {
        var errors = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Parse(
            $"base_url: https://api.test\nendpoints:\n  me: {{ path: /me }}\nsteps:\n  - call: me\n{step}\n", "test.yaml")).Errors;

        Assert.Contains(expected, errors);
    }

    [Fact]
    public void Inspection_ShowsCookieChecks_AndCookieSaves()
    {
        var json = JsonDocument.Parse(ProtocolJson.Inspection("1.2.3", WorkflowLoader.Parse("""
            base_url: https://api.test
            endpoints:
              login: { method: POST, path: /session }
            steps:
              - call: login
                expect:
                  cookies: { sid: { exists: true } }
                save:
                  csrf: { cookie: XSRF-TOKEN }
                  id: $.id
            """, "test.yaml"), indented: false)).RootElement;

        var step = json.GetProperty("steps")[0];
        Assert.Equal("""[{"subject":"cookie sid","condition":"exists"}]""", step.GetProperty("expect").GetProperty("checks").GetRawText());
        Assert.Equal("""{"csrf":"cookie XSRF-TOKEN","id":"$.id"}""", step.GetProperty("save").GetRawText());
    }
}
