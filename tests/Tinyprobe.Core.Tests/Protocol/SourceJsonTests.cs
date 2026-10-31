using System.Text.Json;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Protocol;
using YamlDotNet.RepresentationModel;

namespace Tinyprobe.Core.Tests.Protocol;

/// <summary><c>inspect --json --source</c>: the file as written, and its blocks (docs/specs/0018-inspect-source.md).</summary>
public sealed class SourceJsonTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tinyprobe-source-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private const string Api = """
        # Shop API
        base_url: ${baseUrl}
        auth:
          bearer:
            Authorization: "Bearer ${token}"
          service:
            basic: { username: admin, password: secret }

        endpoints:
          login:
            method: POST
            path: /login            # the token
            body:
              email: ${email}
              remember: true
              attempts: 3
              note: ~
              code: "42"
              empty:

          # Users by id.
          get_user: { path: "/users/${id}", auth: bearer }
        """;

    private const string Workflow = """
        name: Users
        import:
          - api.yaml
        variables:
          email: ada@example.com
        steps:
          - use: login.yaml
            with: { password: secret }

          - name: User by id
            call: get_user
            with:
              id: 1
            expect:
              status: 200
              body:
                $.id: 1
                $.role: { in: [admin, owner] }
            save:
              userId: $.id
          - sleep: 1s
          -
            call: get_user
            for_each: [ { id: 1 }, { id: 2 } ]
        """;

    private string Write(string name, string text)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string Text(string text, JsonElement block)
    {
        var lines = text.Split('\n');
        int Index(JsonElement position) =>
            lines.Take(position.GetProperty("line").GetInt32() - 1).Sum(l => l.Length + 1) + position.GetProperty("column").GetInt32() - 1;
        var start = Index(block.GetProperty("start"));
        return text[start..Index(block.GetProperty("end"))];
    }

    /// <summary>Compact JSON, to compare values whatever the indentation of the document they come from.</summary>
    private static string Compact(JsonElement element) => JsonSerializer.Serialize(element);

    private static JsonElement Literal(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return TemplateRenderer.ToLiteral(stream.Documents[0].RootNode);
    }

    [Fact]
    public void TheSource_IsTheFileAsWritten_TypedAsYamlReadsIt_InOrder()
    {
        var path = Write("api.yaml", Api);

        var json = Parse(ProtocolJson.Inspection("0.21.0", WorkflowLoader.LoadAny(path), sourceFile: path));

        var source = json.GetProperty("source");
        Assert.Equal(["base_url", "auth", "endpoints"], source.EnumerateObject().Select(p => p.Name));
        Assert.Equal("${baseUrl}", source.GetProperty("base_url").GetString());
        Assert.Equal("Bearer ${token}", source.GetProperty("auth").GetProperty("bearer").GetProperty("Authorization").GetString());
        var body = source.GetProperty("endpoints").GetProperty("login").GetProperty("body");
        Assert.Equal("""{"email":"${email}","remember":true,"attempts":3,"note":null,"code":"42","empty":null}""", Compact(body));
        Assert.Equal("""{"path":"/users/${id}","auth":"bearer"}""", Compact(source.GetProperty("endpoints").GetProperty("get_user")));
    }

    [Fact]
    public void Blocks_CutTheFile_AtTopLevelKeysAuthSetsAndEndpoints()
    {
        var path = Write("api.yaml", Api);

        var blocks = Parse(ProtocolJson.Inspection("0.21.0", WorkflowLoader.LoadAny(path), sourceFile: path)).GetProperty("blocks").EnumerateArray().ToList();

        Assert.Equal(
            ["key base_url", "key auth", "auth bearer", "auth service", "key endpoints", "endpoint login", "endpoint get_user"],
            blocks.Select(b => $"{b.GetProperty("kind").GetString()} {b.GetProperty("name").GetString()}"));
        Assert.Equal("base_url: ${baseUrl}", Text(Api, blocks[0]));
        Assert.Equal("bearer:\n    Authorization: \"Bearer ${token}\"", Text(Api, blocks[2]));
        // The comment and the blank line after an endpoint belong to the next one.
        Assert.Equal("login:\n    method: POST\n    path: /login            # the token\n    body:\n      email: ${email}\n      remember: true\n      attempts: 3\n      note: ~\n      code: \"42\"\n      empty:", Text(Api, blocks[5]));
        Assert.Equal("get_user: { path: \"/users/${id}\", auth: bearer }", Text(Api, blocks[6]));
        Assert.Equal(new { line = 10, column = 3 }, new { line = blocks[5].GetProperty("start").GetProperty("line").GetInt32(), column = blocks[5].GetProperty("start").GetProperty("column").GetInt32() });
    }

    [Fact]
    public void EachBlock_ParsesBackToItsSource()
    {
        var path = Write("api.yaml", Api);
        var json = Parse(ProtocolJson.Inspection("0.21.0", WorkflowLoader.LoadAny(path), sourceFile: path));
        var source = json.GetProperty("source");

        foreach (var block in json.GetProperty("blocks").EnumerateArray())
        {
            var name = block.GetProperty("name").GetString()!;
            var expected = block.GetProperty("kind").GetString() switch
            {
                "key" => source.GetProperty(name),
                "auth" => source.GetProperty("auth").GetProperty(name),
                _ => source.GetProperty("endpoints").GetProperty(name)
            };
            // A block is an entry: dedented, it is a one-key mapping.
            var text = Text(Api, block);
            var indent = block.GetProperty("start").GetProperty("column").GetInt32() - 1;
            var dedented = string.Join('\n', text.Split('\n').Select((line, i) => i == 0 || line.Length < indent ? line : line[indent..]));
            Assert.Equal(Compact(expected), Compact(Literal(dedented).GetProperty(name)));
        }
    }

    [Fact]
    public void TheSteps_OfAWorkflow_AreBlocksAsWritten_UseStepsIncluded()
    {
        Write("api.yaml", Api);
        Write("login.yaml", "import:\n  - api.yaml\nparams:\n  password:\nsequence:\n  - call: login\n");
        var path = Write("users.yaml", Workflow);

        var json = Parse(ProtocolJson.Inspection("0.21.0", WorkflowLoader.LoadAny(path), sourceFile: path));

        var steps = json.GetProperty("blocks").EnumerateArray().Where(b => b.GetProperty("kind").GetString() == "step").ToList();
        Assert.Equal([0, 1, 2, 3], steps.Select(s => s.GetProperty("index").GetInt32()));
        Assert.Equal("- use: login.yaml\n    with: { password: secret }", Text(Workflow, steps[0]));
        Assert.StartsWith("- name: User by id\n", Text(Workflow, steps[1]));
        Assert.EndsWith("save:\n      userId: $.id", Text(Workflow, steps[1]));
        Assert.Equal("- sleep: 1s", Text(Workflow, steps[2]));
        Assert.Equal("-\n    call: get_user\n    for_each: [ { id: 1 }, { id: 2 } ]", Text(Workflow, steps[3]));
        Assert.Equal("""{"in":["admin","owner"]}""", Compact(json.GetProperty("source").GetProperty("steps")[1].GetProperty("expect").GetProperty("body").GetProperty("$.role")));
        Assert.Equal(["key name", "key import", "key variables", "key steps"],
            json.GetProperty("blocks").EnumerateArray().Where(b => b.GetProperty("kind").GetString() == "key").Select(b => $"key {b.GetProperty("name").GetString()}"));
    }

    [Fact]
    public void ASequenceFile_HasItsStepsAsBlocks()
    {
        Write("api.yaml", Api);
        var path = Write("login.yaml", "name: Log in\nimport:\n  - api.yaml\nsequence:\n  - call: login\n  - sleep: 1s\n");

        var json = Parse(ProtocolJson.Inspection("0.21.0", WorkflowLoader.LoadAny(path), sourceFile: path));

        Assert.Equal(2, json.GetProperty("blocks").EnumerateArray().Count(b => b.GetProperty("kind").GetString() == "step"));
    }

    [Fact]
    public void AnInvalidFile_WithReadableYaml_HasItsSource_AndAYamlError_HasNone()
    {
        var invalid = Write("bad.yaml", "endpoints:\n  a: { path: /a, auth: missing }\n");
        var broken = Write("broken.yaml", "endpoints:\n  a: [unclosed\n");

        var problems = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.LoadAny(invalid)).Problems;
        var withSource = Parse(ProtocolJson.Invalid("0.21.0", invalid, problems, sourceFile: invalid));
        var brokenProblems = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.LoadAny(broken)).Problems;
        var withoutSource = Parse(ProtocolJson.Invalid("0.21.0", broken, brokenProblems, sourceFile: broken));

        Assert.Equal("missing", withSource.GetProperty("source").GetProperty("endpoints").GetProperty("a").GetProperty("auth").GetString());
        Assert.Equal(["key endpoints", "endpoint a"], withSource.GetProperty("blocks").EnumerateArray().Select(b => $"{b.GetProperty("kind").GetString()} {b.GetProperty("name").GetString()}"));
        Assert.False(withoutSource.TryGetProperty("source", out _));
        Assert.False(withoutSource.TryGetProperty("blocks", out _));
    }

    [Fact]
    public void WithoutSource_TheInspectionHasNoNewKeys()
    {
        var path = Write("api.yaml", Api);

        var json = Parse(ProtocolJson.Inspection("0.21.0", WorkflowLoader.LoadAny(path)));

        Assert.False(json.TryGetProperty("source", out _));
        Assert.False(json.TryGetProperty("blocks", out _));
    }
}
