using System.Net;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Tinyprobe.Core.Models;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Reporting;

namespace Tinyprobe.Core.Tests.Reporting;

public class JunitReportTests
{
    private static readonly DateTime Started = new(2026, 10, 27, 9, 12, 3, DateTimeKind.Utc);

    private const string Workflow = """
        name: Users
        base_url: https://api.test
        endpoints:
          ping: { path: /ping }
        steps:
          - name: List users
            call: ping
          - sleep: 1ms
          - name: Create a user
            call: ping
          - name: Read the user
            call: ping
        """;

    private static JunitRun Run(string yaml, params StepReport[] steps)
    {
        var report = new WorkflowReport();
        report.StepReports.AddRange(steps);
        return new JunitRun("tinyprobe/workflows/users.yaml", WorkflowLoader.Parse(yaml, "users.yaml"), report,
            TimeSpan.FromMilliseconds(1020), Started);
    }

    private static StepReport Passed(int index, string name) => new()
    {
        Index = index, StepName = name, Method = "GET", Url = new Uri("https://api.test/ping"),
        StatusCode = HttpStatusCode.OK, Duration = TimeSpan.FromMilliseconds(20)
    };

    private static StepReport FailedStatus(int index) => new()
    {
        Index = index, StepName = "Create a user", Method = "POST", Url = new Uri("https://api.test/users"),
        StatusCode = HttpStatusCode.InternalServerError, IsSuccess = false, FailureReason = "Expected status 201, got 500",
        Duration = TimeSpan.FromMilliseconds(31),
        Assertions =
        {
            new() { Subject = "$.email", Condition = "== \"a@b.c\"", ActualValue = "\"a@b.c\"", Passed = true },
            new() { Subject = "$.id", Condition = "exists", ActualValue = "(missing)", Passed = false }
        },
        Request = new RecordedRequest { Method = "POST", Url = "https://api.test/users", Headers = [new("Authorization", "Bearer top-secret-token")], Body = "{\"password\":\"hunter2\"}" },
        Response = new RecordedResponse { Status = 500, Headers = [], Body = "{\"token\":\"another-secret\"}" }
    };

    private static XElement Parse(string xml) => XElement.Parse(xml);

    [Fact]
    public void Steps_AreTestCases_WithFailureAndSkipped()
    {
        var xml = Parse(JunitReport.Write("1.2.3", [Run(Workflow, Passed(0, "List users"), FailedStatus(2))]));

        Assert.Equal(("3", "1", "0", "1"), ((string)xml.Attribute("tests")!, (string)xml.Attribute("failures")!, (string)xml.Attribute("errors")!, (string)xml.Attribute("skipped")!));
        var suite = Assert.Single(xml.Elements("testsuite"));
        Assert.Equal("Users", (string)suite.Attribute("name")!);
        Assert.Equal("tinyprobe/workflows/users.yaml", (string)suite.Attribute("file")!);
        Assert.Equal("1.02", (string)suite.Attribute("time")!);
        Assert.Equal("2026-10-27T09:12:03Z", (string)suite.Attribute("timestamp")!);
        Assert.Equal("1.2.3", (string)suite.Element("properties")!.Element("property")!.Attribute("value")!);

        var cases = suite.Elements("testcase").ToList();
        Assert.Equal(["List users", "Create a user", "Read the user"], cases.Select(c => (string)c.Attribute("name")!));
        Assert.All(cases, c => Assert.Equal("Users", (string)c.Attribute("classname")!));
        Assert.Equal(["6", "9", "11"], cases.Select(c => (string)c.Attribute("line")!));
        Assert.Equal("0.02", (string)cases[0].Attribute("time")!);
        Assert.Empty(cases[0].Elements());

        var failure = cases[1].Element("failure")!;
        Assert.Equal("Expected status 201, got 500", (string)failure.Attribute("message")!);
        Assert.Equal("status", (string)failure.Attribute("type")!);
        Assert.Equal("""
            POST https://api.test/users -> 500
            Expected status 201, got 500

            ✓ $.email == "a@b.c"
            ✗ $.id exists  actual (missing)
            """.ReplaceLineEndings("\n"), failure.Value);

        Assert.Equal("Not run: an earlier step failed", (string)cases[2].Element("skipped")!.Attribute("message")!);
    }

    [Fact]
    public void NoResponse_IsAnError_AndAFailedCheck_IsACheckFailure()
    {
        var timeout = new StepReport { Index = 0, StepName = "List users", Method = "GET", Url = new Uri("https://api.test/ping"), IsSuccess = false, FailureReason = "Request timed out" };
        var check = Passed(2, "Create a user");
        check.IsSuccess = false;
        check.Assertions.Add(new() { Subject = "$.id", Condition = ">= 1", ActualValue = "0", Passed = false });

        var cases = Parse(JunitReport.Write("1.2.3", [Run(Workflow, timeout), Run(Workflow, Passed(0, "List users"), check)]))
            .Elements("testsuite").SelectMany(s => s.Elements("testcase")).ToList();

        var error = cases[0].Element("error")!;
        Assert.Equal("Request timed out", (string)error.Attribute("message")!);
        Assert.Equal("request", (string)error.Attribute("type")!);
        Assert.Contains("GET https://api.test/ping -> no response", error.Value);

        var failure = cases[4].Element("failure")!;
        Assert.Equal("$.id >= 1, actual 0", (string)failure.Attribute("message")!);
        Assert.Equal("check", (string)failure.Attribute("type")!);
    }

    [Fact]
    public void Report_HasNoHeadersAndNoBodies()
    {
        var xml = JunitReport.Write("1.2.3", [Run(Workflow, Passed(0, "List users"), FailedStatus(2))]);

        Assert.DoesNotContain("top-secret-token", xml);
        Assert.DoesNotContain("hunter2", xml);
        Assert.DoesNotContain("another-secret", xml);
        Assert.DoesNotContain("Authorization", xml);
    }

    [Fact]
    public void TextIsEscaped_AndCharactersXmlCannotHoldAreReplaced()
    {
        var step = Passed(0, "List <users> & \"friends\"");
        step.IsSuccess = false;
        step.Assertions.Add(new() { Subject = "$.name", Condition = "== \"a\"", ActualValue = "\"\u0001bell\"", Passed = false });

        var xml = JunitReport.Write("1.2.3", [Run(Workflow, step)]);
        var testCase = Parse(xml).Descendants("testcase").First();

        Assert.Equal("List <users> & \"friends\"", (string)testCase.Attribute("name")!);
        Assert.Contains("\"?bell\"", testCase.Element("failure")!.Value);
    }

    [Fact]
    public void InvalidFiles_AreOneErrorEach_WithTheirProblems()
    {
        var xml = Parse(JunitReport.WriteInvalid("1.2.3", [
            new LoadProblem("tinyprobe/workflows/users.yaml", 14, 11, "unknown endpoint 'get_usr' (did you mean 'get_user'?)"),
            new LoadProblem("tinyprobe/workflows/users.yaml", 20, 5, "unknown key 'expcet' in a step (did you mean 'expect'?)"),
            new LoadProblem("tinyprobe/api/backend.yaml", 3, 3, "unknown key 'pth' in an endpoint (did you mean 'path'?)")
        ]));

        Assert.Equal(("2", "2"), ((string)xml.Attribute("tests")!, (string)xml.Attribute("errors")!));
        var first = xml.Elements("testsuite").First();
        Assert.Equal("tinyprobe/workflows/users.yaml", (string)first.Attribute("name")!);
        var error = first.Element("testcase")!.Element("error")!;
        Assert.Equal("The file is not valid (2 problems)", (string)error.Attribute("message")!);
        Assert.Equal(
            "tinyprobe/workflows/users.yaml:14:11: unknown endpoint 'get_usr' (did you mean 'get_user'?)\n" +
            "tinyprobe/workflows/users.yaml:20:5: unknown key 'expcet' in a step (did you mean 'expect'?)",
            error.Value);
    }

    [Fact]
    public void Report_IsValidJenkinsJunit_ApartFromCommonExtensions()
    {
        // junit-10.xsd is the schema of the Jenkins xUnit plugin (MIT). It has no file and line on testcase and no
        // skipped on testsuites: extensions that GitLab and the GitHub test-report actions read. They are removed
        // before validating; everything else must match.
        var schemas = new XmlSchemaSet();
        schemas.Add(null, Path.Combine(AppContext.BaseDirectory, "Reporting", "junit-10.xsd"));

        foreach (var xml in new[]
        {
            JunitReport.Write("1.2.3", [Run(Workflow, Passed(0, "List users"), FailedStatus(2))]),
            JunitReport.WriteInvalid("1.2.3", [new LoadProblem("w.yaml", 3, 5, "unknown endpoint 'nope'")])
        })
        {
            var document = XDocument.Parse(xml);
            document.Root!.Attribute("skipped")?.Remove();
            foreach (var testCase in document.Descendants("testcase"))
            {
                testCase.Attribute("file")?.Remove();
                testCase.Attribute("line")?.Remove();
            }

            var errors = new List<string>();
            document.Validate(schemas, (_, e) => errors.Add(e.Message));
            Assert.Empty(errors);
        }
    }
}
