using System.Text.Json;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Models;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Protocol;
using Tinyprobe.Core.Tests.Fakes;

namespace Tinyprobe.Core.Tests.Execution;

/// <summary><c>form</c> and <c>multipart</c> bodies (docs/specs/0008-form-and-multipart-bodies.md).</summary>
public sealed class FormAndMultipartTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tinyprobe-multipart-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private const string Upload = """
        base_url: https://api.test
        headers:
          Content-Type: application/json
        endpoints:
          upload:
            method: POST
            path: /files
            params: { description:, avatar: }
            multipart:
              description: ${description}
              avatar:
                file: ${avatar}
              tags: [profile, public]
        """;

    private string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Runs a workflow saved as <c>workflows/test.yaml</c> in the temporary directory.</summary>
    private async Task<(WorkflowReport Report, FakeServer Server)> RunAsync(string yaml, string? fileRoot = null)
    {
        var workflow = WorkflowLoader.Load(WriteFile("workflows/test.yaml", yaml));
        var server = new FakeServer();
        var engine = new WorkflowEngine(new HttpClient(server)) { RecordExchanges = true, FileRoot = fileRoot };
        return (await engine.ExecuteAsync(workflow), server);
    }

    private static WorkflowLoadException LoadFails(string yaml) =>
        Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Parse(yaml, "test.yaml"));

    // --- form ------------------------------------------------------------------

    [Fact]
    public async Task Form_EncodesValues_RepeatsLists_AndSetsItsContentType()
    {
        var (report, server) = await RunAsync("""
            base_url: https://api.test
            headers:
              Content-Type: application/json
            endpoints:
              token:
                method: POST
                path: /token
                params: { secret:, scopes: }
                form:
                  grant_type: client_credentials
                  client_secret: ${secret}
                  scope: ${scopes}
                  count: 3
                  empty: null
                  tags: [a b, c]
            steps:
              - call: token
                with: { secret: "a&b=c+d", scopes: [read, write] }
            """);

        Assert.True(report.Single().IsSuccess, report.Single().FailureReason);
        var request = server.Requests.Single();
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
        Assert.Equal("grant_type=client_credentials&client_secret=a%26b%3Dc%2Bd&scope=read&scope=write&count=3&empty=&tags=a+b&tags=c", request.Body);
        Assert.Equal(request.Body, report.Single().Request!.Body);
    }

    // --- multipart -------------------------------------------------------------

    [Fact]
    public async Task Multipart_SendsFieldsInOrder_WithTheFileRelativeToTheWorkflow()
    {
        WriteFile("fixtures/avatar.png", "PNGDATA");

        var (report, server) = await RunAsync($$"""
            {{Upload}}
            steps:
              - call: upload
                with: { description: Profile picture, avatar: ../fixtures/avatar.png }
            """);

        Assert.True(report.Single().IsSuccess, report.Single().FailureReason);
        var request = server.Requests.Single();
        Assert.StartsWith("multipart/form-data; boundary=\"tinyprobe-", request.ContentType);
        var boundary = request.ContentType!.Split("boundary=")[1].Trim('"');

        Assert.Equal(string.Join("\r\n",
            $"--{boundary}",
            "Content-Disposition: form-data; name=\"description\"",
            "",
            "Profile picture",
            $"--{boundary}",
            "Content-Disposition: form-data; name=\"avatar\"; filename=\"avatar.png\"",
            "Content-Type: image/png",
            "",
            "PNGDATA",
            $"--{boundary}",
            "Content-Disposition: form-data; name=\"tags\"",
            "",
            "profile",
            $"--{boundary}",
            "Content-Disposition: form-data; name=\"tags\"",
            "",
            "public",
            $"--{boundary}--",
            ""), request.Body);
    }

    [Fact]
    public async Task Multipart_RecordsEachFileAsOneLine()
    {
        WriteFile("fixtures/avatar.png", new string('x', 1300));

        var (report, _) = await RunAsync($$"""
            {{Upload}}
            steps:
              - call: upload
                with: { description: Me, avatar: ../fixtures/avatar.png }
            """);

        var recorded = report.Single().Request!.Body!;
        var boundary = recorded.Split('\n')[0][2..];
        Assert.Equal(string.Join("\n",
            $"--{boundary}",
            "Content-Disposition: form-data; name=\"description\"",
            "",
            "Me",
            $"--{boundary}",
            "Content-Disposition: form-data; name=\"avatar\"; filename=\"avatar.png\"",
            "Content-Type: image/png",
            "",
            "(file ../fixtures/avatar.png, 1.3 KB)",
            $"--{boundary}",
            "Content-Disposition: form-data; name=\"tags\"",
            "",
            "profile",
            $"--{boundary}",
            "Content-Disposition: form-data; name=\"tags\"",
            "",
            "public",
            $"--{boundary}--"), recorded);
    }

    [Theory]
    [InlineData("data.csv", null, null, "data.csv", "text/csv")]
    [InlineData("data.bin", null, null, "data.bin", "application/octet-stream")]
    [InlineData("data.csv", "report.csv", "text/plain; charset=utf-8", "report.csv", "text/plain; charset=utf-8")]
    public async Task Multipart_FileNameAndContentType_DefaultToTheFile(
        string file, string? fileName, string? contentType, string expectedName, string expectedType)
    {
        WriteFile($"workflows/{file}", "1,2");
        var options = (fileName is null ? "" : $", filename: {fileName}") + (contentType is null ? "" : $", content_type: \"{contentType}\"");

        var (report, server) = await RunAsync($$"""
            base_url: https://api.test
            endpoints:
              upload:
                method: POST
                path: /files
                multipart:
                  data: { file: {{file}}{{options}} }
            steps:
              - call: upload
            """);

        Assert.True(report.Single().IsSuccess, report.Single().FailureReason);
        Assert.Contains($"filename=\"{expectedName}\"\r\nContent-Type: {expectedType}\r\n", server.Requests.Single().Body);
    }

    [Fact]
    public async Task Multipart_MissingFile_FailsTheStepBeforeSending()
    {
        var (report, server) = await RunAsync($$"""
            {{Upload}}
            steps:
              - call: upload
                with: { description: Me, avatar: ../fixtures/nope.png }
            """);

        Assert.Empty(server.Requests);
        Assert.Equal("Cannot read the file for multipart field 'avatar': ../fixtures/nope.png was not found", report.Single().FailureReason);
    }

    [Fact]
    public async Task Multipart_UnderMcp_OnlySendsFilesInsideTheWorkingDirectory()
    {
        WriteFile("workflows/inside.png", "ok");
        var outside = Path.Combine(Path.GetTempPath(), $"tinyprobe-outside-{Guid.NewGuid():N}.png");
        File.WriteAllText(outside, "secret");
        try
        {
            var root = Path.Combine(_directory, "workflows");
            var (inside, _) = await RunAsync($"{Upload}\nsteps:\n  - call: upload\n    with: {{ description: x, avatar: inside.png }}\n", fileRoot: root);
            Assert.True(inside.Single().IsSuccess, inside.Single().FailureReason);

            var (report, server) = await RunAsync($"{Upload}\nsteps:\n  - call: upload\n    with: {{ description: x, avatar: \"{outside}\" }}\n", fileRoot: root);
            Assert.Empty(server.Requests);
            Assert.Equal($"Cannot read the file for multipart field 'avatar': {outside} is outside the working directory of the MCP server", report.Single().FailureReason);

            var (sibling, _) = await RunAsync($"{Upload}\nsteps:\n  - call: upload\n    with: {{ description: x, avatar: ../workflows-other/a.png }}\n", fileRoot: root);
            Assert.Contains("is outside the working directory", sibling.Single().FailureReason);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task Call_ReadsFilesRelativeToTheApiFile()
    {
        WriteFile("api/fixtures/a.txt", "hello");
        var api = WorkflowLoader.LoadAny(WriteFile("api/backend.yaml", Upload));
        var call = WorkflowLoader.CreateCall(api, "upload", [new("description", "x"), new("avatar", "fixtures/a.txt")]);
        var server = new FakeServer();

        var report = await new WorkflowEngine(new HttpClient(server)).ExecuteAsync(call);

        Assert.True(report.Single().IsSuccess, report.Single().FailureReason);
        Assert.Contains("filename=\"a.txt\"\r\nContent-Type: text/plain\r\n\r\nhello\r\n", server.Requests.Single().Body);
    }

    // --- validation ------------------------------------------------------------

    [Theory]
    [InlineData("    body: {}\n    multipart: { a: b }", "test.yaml:2:3: endpoint 'x' has both body and multipart: use one of body, form, multipart")]
    [InlineData("    form: { a: b }\n    multipart: { a: b }", "test.yaml:2:3: endpoint 'x' has both form and multipart: use one of body, form, multipart")]
    [InlineData("    multipart:\n      avatar: { file: a.png, filname: b.png }", "test.yaml:6:30: unknown key 'filname' in a multipart file (did you mean 'filename'?)")]
    [InlineData("    multipart:\n      avatar: { filename: b.png }", "test.yaml:6:15: a multipart file needs file: the path of the file to send")]
    [InlineData("    multipart:\n      meta: [[a]]", "test.yaml:6:14: multipart field 'meta' must be a value, a file ({ file: path }) or a list of them")]
    [InlineData("    form:\n      tags: { a: b }", "test.yaml:6:13: form field 'tags' must be a value or a list of values")]
    [InlineData("    form:\n      tags: [[a]]", "test.yaml:6:14: form field 'tags' must be a value or a list of values")]
    [InlineData("    headers: { content-type: text/plain }\n    multipart: { a: b }", "test.yaml:5:30: endpoint 'x' sets content-type, but a multipart body sets its own: remove the header")]
    [InlineData("    headers: { Content-Type: text/plain }\n    form: { a: b }", "test.yaml:5:30: endpoint 'x' sets Content-Type, but a form body sets its own: remove the header")]
    public void InvalidBodies_AreReportedWithTheirLocation(string body, string expected)
    {
        var errors = LoadFails($"endpoints:\n  x:\n    method: POST\n    path: /x\n{body}\nsteps:\n  - call: x\n").Errors;

        Assert.Contains(expected, errors);
    }

    [Fact]
    public void Placeholders_OfFormAndMultipart_AreArguments()
    {
        var errors = LoadFails($$"""
            {{Upload}}
            steps:
              - call: upload
                with: { description: x, avatar: a.png, avatr: b.png }
            """).Errors;

        Assert.Equal("test.yaml:16:51: unknown argument 'avatr' for endpoint 'upload' (did you mean 'avatar'?)", errors.Single());
    }

    // --- inspect ---------------------------------------------------------------

    [Fact]
    public void Inspection_ListsTheFields()
    {
        var json = JsonDocument.Parse(ProtocolJson.Inspection("1.2.3", WorkflowLoader.LoadAny(WriteFile("api.yaml", $$"""
            {{Upload}}
              token:
                method: POST
                path: /token
                form: { grant_type: client_credentials, n: 1 }
              plain:
                path: /plain
            """)), indented: false)).RootElement;

        var endpoints = json.GetProperty("endpoints").EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!);
        Assert.Equal(
            """[{"name":"description","value":"${description}"},{"name":"avatar","file":"${avatar}","filename":null,"content_type":null},{"name":"tags","value":"profile"},{"name":"tags","value":"public"}]""",
            endpoints["upload"].GetProperty("multipart").GetRawText());
        Assert.Equal(JsonValueKind.Null, endpoints["upload"].GetProperty("form").ValueKind);
        Assert.Equal("""[{"name":"grant_type","value":"client_credentials"},{"name":"n","value":1}]""", endpoints["token"].GetProperty("form").GetRawText());
        Assert.Equal(JsonValueKind.Null, endpoints["plain"].GetProperty("multipart").ValueKind);
    }
}
