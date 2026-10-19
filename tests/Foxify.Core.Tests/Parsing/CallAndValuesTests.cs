using System.Net;
using System.Text.Json;
using Foxify.Core.Definitions;
using Foxify.Core.Execution;
using Foxify.Core.Parsing;
using Foxify.Core.Tests.Fakes;

namespace Foxify.Core.Tests.Parsing;

/// <summary><c>foxify inspect</c> and <c>foxify call</c>: API files, one-off calls and command-line values.</summary>
public sealed class CallAndValuesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("foxify-call-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string Api = """
        base_url: https://api.test
        endpoints:
          get_user:
            path: /users/${id}
          search:
            path: /users
            query: [name, page]
          create_user:
            method: POST
            path: /users
            params:
              name:
              role: user
            body:
              name: ${name}
              role: ${role}
        """;

    private static WorkflowDefinition Call(WorkflowDefinition source, string endpoint, params (string Name, string Value)[] arguments) =>
        WorkflowLoader.CreateCall(source, endpoint, arguments.Select(a => new KeyValuePair<string, string>(a.Name, a.Value)).ToList());

    [Fact]
    public void LoadAny_ReadsAnApiFileAsAWorkflowWithoutSteps()
    {
        var api = WorkflowLoader.LoadAny(Write("api.yaml", Api));

        Assert.Equal("api", api.Name);
        Assert.Empty(api.Steps);
        Assert.Equal(["get_user", "search", "create_user"], api.Endpoints.Keys);
    }

    [Fact]
    public void LoadAny_ValidatesAWorkflowCompletely()
    {
        var path = Write("flow.yaml", $"{Api}\nsteps:\n  - call: get_usr\n");

        var error = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.LoadAny(path)).Problems.Single();

        Assert.Contains("unknown endpoint 'get_usr' (did you mean 'get_user'?)", error.Message);
    }

    [Fact]
    public void LoadAny_ReportsUnknownKeysOfAnApiFile()
    {
        var path = Write("api.yaml", "endpoint:\n  ping: { path: /ping }\n");

        var error = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.LoadAny(path)).Problems.Single();

        Assert.Equal((1, 1), (error.Line, error.Column));
        Assert.Equal("unknown key 'endpoint' in an API file (did you mean 'endpoints'?)", error.Message);
    }

    [Fact]
    public async Task CreateCall_SendsTypedArgumentsAndAcceptsAnyStatus()
    {
        var api = WorkflowLoader.LoadAny(Write("api.yaml", Api));
        var call = Call(api, "create_user", ("name", "\"42\""));
        var server = new FakeServer(_ => new FakeResponse(HttpStatusCode.Conflict));

        var report = await new WorkflowEngine(new HttpClient(server)).ExecuteAsync(call);

        Assert.True(report.Single().IsSuccess);
        Assert.Equal(HttpStatusCode.Conflict, report.Single().StatusCode);
        Assert.Equal("""{"name":"42","role":"user"}""", server.Requests.Single().Body);
    }

    [Fact]
    public async Task CreateCall_ArgumentsCanUseVariablesAndQuery()
    {
        var api = WorkflowLoader.LoadAny(Write("api.yaml", Api));
        var call = Call(api, "search", ("name", "${who}"), ("page", "2"));
        var server = new FakeServer();
        var environment = new Dictionary<string, JsonElement> { ["who"] = WorkflowLoader.ParseYamlValue("ada lovelace") };

        await new WorkflowEngine(new HttpClient(server)).ExecuteAsync(call, environment);

        Assert.Equal("https://api.test/users?name=ada%20lovelace&page=2", server.Requests.Single().Url);
        Assert.Equal("search name=${who} page=2", call.Steps.OfType<CallStep>().Single().Name);
    }

    [Fact]
    public void CreateCall_ValidatesLikeAStep()
    {
        var api = WorkflowLoader.LoadAny(Write("api.yaml", Api));

        var unknown = Assert.Throws<WorkflowLoadException>(() => Call(api, "get_usr")).Errors.Single();
        var missing = Assert.Throws<WorkflowLoadException>(() => Call(api, "create_user")).Errors.Single();
        var typo = Assert.Throws<WorkflowLoadException>(() => Call(api, "get_user", ("id", "1"), ("idd", "2"))).Errors.Single();
        var generator = Assert.Throws<WorkflowLoadException>(() => Call(api, "get_user", ("id", "${$uid}"))).Errors.Single();

        Assert.Equal("command line: unknown endpoint 'get_usr' (did you mean 'get_user'?)", unknown);
        Assert.StartsWith("command line: missing required param 'name' of endpoint 'create_user'", missing);
        Assert.Equal("command line: unknown argument 'idd' for endpoint 'get_user' (did you mean 'id'?)", typo);
        Assert.Contains("(did you mean '$uuid'?)", generator);
    }

    [Fact]
    public void CreateCall_KeepsTheVariablesAndSettingsOfAWorkflow()
    {
        var flow = WorkflowLoader.LoadAny(Write("flow.yaml", $"""
            {Api}
            variables:
              userId: 7
            settings:
              timeout: 5
            steps:
              - call: get_user
                with:
                  id: 1
            """));

        var call = Call(flow, "get_user", ("id", "${userId}"));

        Assert.Single(call.Steps);
        Assert.Equal(["userId"], call.Variables.Keys);
        Assert.Equal(5, call.Settings.Timeout);
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("\"42\"", "\"42\"")]
    [InlineData("true", "true")]
    [InlineData("ada", "\"ada\"")]
    [InlineData("", "\"\"")]
    [InlineData("null", "null")]
    [InlineData("[a, 2]", "[\"a\",2]")]
    [InlineData("{ x: 1 }", "{\"x\":1}")]
    [InlineData("${token}", "\"${token}\"")]
    [InlineData("a=b", "\"a=b\"")]
    public void ParseYamlValue_TypesValuesLikeYaml(string text, string json)
    {
        Assert.Equal(json, WorkflowLoader.ParseYamlValue(text).GetRawText());
    }
}
