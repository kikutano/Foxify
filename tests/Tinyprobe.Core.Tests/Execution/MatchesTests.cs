using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Tests.Fakes;

namespace Tinyprobe.Core.Tests.Execution;

/// <summary>The <c>matches</c> operator (docs/specs/0015-matches.md).</summary>
public sealed class MatchesTests
{
    private const string Api = """
        base_url: https://api.test
        endpoints:
          order: { path: /order }
        """;

    private const string Order = """
        {
          "id": "ORD-20261029-0042",
          "number": 42,
          "price": 9.5,
          "paid": true,
          "email": "Ada@Example.com",
          "note": null,
          "tags": ["new"],
          "address": { "city": "Bari" },
          "lines": "first\nsecond"
        }
        """;

    private static Task<(Models.WorkflowReport Report, FakeServer Server)> Run(string expect, string body = Order) =>
        Fakes.Run.Workflow($$"""
            {{Api}}
            variables:
              prefix: ORD
            steps:
              - call: order
                expect:
                  {{expect}}
            """,
            respond: _ => new FakeResponse(Body: body)
            {
                Headers = { ["Location"] = "/api/orders/42" },
                SetCookies = ["sid=0123456789abcdef0123456789abcdef; Path=/"]
            });

    [Theory]
    [InlineData("$.id: { matches: '^ORD-[0-9]{8}-[0-9]{4}$' }", true)]
    [InlineData("$.id: { matches: '^ORD-[0-9]{3}$' }", false)]
    [InlineData("$.id: { matches: '0042' }", true)]                       // searches the value, like JavaScript
    [InlineData("$.id: { matches: '^0042' }", false)]
    [InlineData("$.email: { matches: '@example\\.com$' }", false)]       // case matters...
    [InlineData("$.email: { matches: '(?i)@example\\.com$' }", true)]    // ...unless (?i)
    [InlineData("$.lines: { matches: '^second$' }", false)]
    [InlineData("$.lines: { matches: '(?m)^second$' }", true)]
    [InlineData("$.number: { matches: '^[0-9]+$' }", true)]               // a number as its JSON text
    [InlineData("$.price: { matches: '^9\\.5$' }", true)]
    [InlineData("$.paid: { matches: '^true$' }", true)]
    [InlineData("$.note: { matches: '.*' }", false)]                      // null, objects and arrays never match
    [InlineData("$.tags: { matches: 'new' }", false)]
    [InlineData("$.address: { matches: 'Bari' }", false)]
    [InlineData("$.missing: { matches: '.*' }", false)]
    [InlineData("$.id: { matches: ORD }", true)]                          // unquoted works for a plain pattern
    [InlineData("$.id: { matches: '^${prefix}-' }", true)]               // placeholders, replaced at run time
    [InlineData("$.id: { matches: '^ORD', contains: '0042', exists: true }", true)]
    [InlineData("$.id: { matches: '^ORD', contains: 'nope' }", false)]
    [InlineData("$.id: { any: [ { matches: '^INV-' }, { matches: '^ORD-' } ] }", true)]
    [InlineData("$.id: { any: [ { matches: '^INV-' }, null ] }", false)]
    public async Task BodyValues(string check, bool passed)
    {
        var (report, _) = await Run($"body:\n        {check}");

        Assert.Equal(passed, report.Single().IsSuccess);
    }

    [Theory]
    [InlineData("headers:\n        Location: { matches: '^/api/orders/[0-9]+$' }", true)]
    [InlineData("headers:\n        Location: { matches: '^/api/users/' }", false)]
    [InlineData("headers:\n        X-Missing: { matches: '.*' }", false)]
    [InlineData("cookies:\n        sid: { matches: '^[0-9a-f]{32}$' }", true)]
    [InlineData("cookies:\n        sid: { matches: '^[0-9]{32}$' }", false)]
    public async Task HeadersAndCookies(string check, bool passed)
    {
        var (report, _) = await Run(check);

        Assert.Equal(passed, report.Single().IsSuccess);
    }

    [Fact]
    public async Task TheReport_ShowsThePatternAndTheActualValue()
    {
        var (report, _) = await Run("body:\n        $.id: { matches: '^ORD-[0-9]{3}$' }");

        var check = report.Single().Assertions.Single();
        Assert.Equal("$.id", check.Subject);
        Assert.Equal("matches \"^ORD-[0-9]{3}$\"", check.Condition);
        Assert.Equal("\"ORD-20261029-0042\"", check.ActualValue);
    }

    [Fact]
    public async Task Until_RepeatsTheCall_UntilTheValueMatches()
    {
        var calls = 0;
        var (report, server) = await Fakes.Run.Workflow($$"""
            {{Api}}
            steps:
              - call: order
                until:
                  body: { $.status: { matches: '^(done|failed)$' } }
                  every: 10ms
                  timeout: 5s
            """,
            respond: _ => new FakeResponse(Body: ++calls < 3 ? """{"status":"running"}""" : """{"status":"done"}"""));

        Assert.True(report.Single().IsSuccess);
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task APatternWithPlaceholders_ThatIsInvalid_FailsTheStepWithItsReason()
    {
        var (report, _) = await Run("body:\n        $.id: { matches: '^${prefix}[' }");

        var step = report.Single();
        Assert.False(step.IsSuccess);
        Assert.Equal("$.id matches \"^ORD[\": invalid regular expression '^ORD[': Unterminated [] set", step.FailureReason);
    }

    [Fact]
    public async Task APatternThatTakesTooLong_FailsTheStep_InsteadOfHangingTheRun()
    {
        var body = $$"""{"text":"{{new string('a', 40)}}!"}""";

        var (report, _) = await Run("body:\n        $.text: { matches: '^(a+)+$' }", body);

        var step = report.Single();
        Assert.False(step.IsSuccess);
        Assert.Equal("$.text matches \"^(a+)+$\": the regular expression took more than 1s on this value", step.FailureReason);
    }

    [Theory]
    [InlineData("$.id: { matches: '^ORD-[0-9' }", "test.yaml:8:28: invalid regular expression '^ORD-[0-9': Unterminated [] set")]
    [InlineData("$.id: { matches: '(?<x>a' }", "test.yaml:8:28: invalid regular expression '(?<x>a': Not enough )'s")]
    [InlineData("$.id: { matches: [a, b] }", "test.yaml:8:28: matches needs a regular expression as text, e.g. { matches: '^ORD-[0-9]+$' }")]
    [InlineData("$.id: { matches: { x: 1 } }", "test.yaml:8:28: matches needs a regular expression as text, e.g. { matches: '^ORD-[0-9]+$' }")]
    public void InvalidPatterns_AreReportedBeforeAnyRequest(string check, string message)
    {
        var yaml = $"{Api}\nsteps:\n  - call: order\n    expect:\n        body:\n          {check}\n";

        var errors = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Parse(yaml, Path.Combine(Path.GetTempPath(), "test.yaml"))).Errors;

        Assert.EndsWith(message, Assert.Single(errors));
    }
}
