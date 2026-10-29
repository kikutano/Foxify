using System.Net;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Models;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Tests.Fakes;

namespace Tinyprobe.Core.Tests.Execution;

/// <summary>The <c>schema</c> operator (docs/specs/0016-json-schema.md).</summary>
public sealed class SchemaTests
{
    private readonly string _root = Directory.CreateTempSubdirectory("tinyprobe-schema-").FullName;

    private const string OpenApi = """
        openapi: 3.0.3
        components:
          schemas:
            Address:
              type: object
              required: [street, city]
              properties:
                street: { type: string }
                city: { type: string }
            User:
              type: object
              required: [id, email, address]
              additionalProperties: false
              properties:
                id: { type: integer, minimum: 1 }
                email: { type: string, format: email }
                nickname: { type: string, nullable: true }
                address: { $ref: "#/components/schemas/Address" }
        """;

    private const string User = """{"id":1,"email":"ada@example.com","nickname":null,"address":{"street":"Via Roma 1","city":"Bari"}}""";

    public SchemaTests()
    {
        Write("openapi.yaml", OpenApi);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private Definitions.WorkflowDefinition Load(string checks) => WorkflowLoader.Parse($$"""
        base_url: https://api.test
        endpoints:
          item: { path: /item }
        steps:
          - call: item
            expect:
              body:
                {{checks}}
        """, Path.Combine(_root, "workflows", "test.yaml"));

    private async Task<StepReport> Run(string checks, string body)
    {
        var workflow = Load(checks);
        var server = new FakeServer(_ => new FakeResponse(Body: body));
        var report = await new WorkflowEngine(new HttpClient(server)).ExecuteAsync(workflow);
        return report.Single();
    }

    private string LoadError(string checks) =>
        Assert.Single(Assert.Throws<WorkflowLoadException>(() => Load(checks)).Errors);

    [Fact]
    public async Task AnOpenApiSchema_WithItsRef_ChecksTheWholeBody()
    {
        var step = await Run("$: { schema: ../openapi.yaml#/components/schemas/User }", User);

        Assert.True(step.IsSuccess);
        var check = Assert.Single(step.Assertions);
        Assert.Equal(("$", "schema ../openapi.yaml#/components/schemas/User", "{…}"), (check.Subject, check.Condition, check.ActualValue));
    }

    [Fact]
    public async Task EachProblem_IsAFailedCheck_AtThePathOfTheValue()
    {
        var body = """{"id":0,"email":42,"nickname":"ada","extra":true,"address":{"street":"Via Roma 1"}}""";

        var step = await Run("$: { schema: ../openapi.yaml#/components/schemas/User }", body);

        Assert.False(step.IsSuccess);
        Assert.Equal(
        [
            ("$.extra", "schema additionalProperties false", "true"),
            ("$.id", "schema minimum 1", "0"),
            ("$.email", "schema type \"string\"", "42"),
            ("$.address.city", "schema required", "(missing)")
        ], step.Assertions.Select(a => (a.Subject, a.Condition, a.ActualValue)).ToList());
        Assert.All(step.Assertions, a => Assert.False(a.Passed));
    }

    [Theory]
    // types; integer is a number without a fraction; nullable (OpenAPI 3.0)
    [InlineData("{ type: integer }", "1.0", true)]
    [InlineData("{ type: integer }", "1.5", false)]
    [InlineData("{ type: [string, null] }", "null", true)]
    [InlineData("{ type: string, nullable: true }", "null", true)]
    [InlineData("{ type: string }", "null", false)]
    [InlineData("{ type: number }", "\"1\"", false)]
    [InlineData("{ enum: [a, b] }", "\"b\"", true)]
    [InlineData("{ enum: [a, b] }", "\"c\"", false)]
    [InlineData("{ const: 3 }", "3.0", true)]
    // combining
    [InlineData("{ allOf: [ { type: number }, { minimum: 2 } ] }", "3", true)]
    [InlineData("{ allOf: [ { type: number }, { minimum: 5 } ] }", "3", false)]
    [InlineData("{ anyOf: [ { type: string }, { minimum: 5 } ] }", "3", false)]
    [InlineData("{ anyOf: [ { type: string }, { minimum: 2 } ] }", "3", true)]
    [InlineData("{ oneOf: [ { type: number }, { minimum: 2 } ] }", "3", false)]   // both match: not one
    [InlineData("{ oneOf: [ { type: string }, { minimum: 2 } ] }", "3", true)]
    [InlineData("{ not: { type: string } }", "3", true)]
    [InlineData("{ not: { type: number } }", "3", false)]
    [InlineData("{ properties: { a: false } }", "{\"a\":1}", false)]
    // objects
    [InlineData("{ required: [a], properties: { a: { type: string } } }", "{\"a\":\"x\"}", true)]
    [InlineData("{ additionalProperties: { type: number } }", "{\"a\":1,\"b\":\"x\"}", false)]
    [InlineData("{ patternProperties: { '^x-': { type: string } }, additionalProperties: false }", "{\"x-a\":\"1\"}", true)]
    [InlineData("{ patternProperties: { '^x-': { type: string } }, additionalProperties: false }", "{\"y\":\"1\"}", false)]
    [InlineData("{ minProperties: 2 }", "{\"a\":1}", false)]
    [InlineData("{ maxProperties: 1 }", "{\"a\":1}", true)]
    // arrays
    [InlineData("{ items: { type: integer }, minItems: 1, maxItems: 3 }", "[1,2]", true)]
    [InlineData("{ items: { type: integer } }", "[1,\"2\"]", false)]
    [InlineData("{ maxItems: 1 }", "[1,2]", false)]
    [InlineData("{ items: [ { type: string }, { type: number } ], additionalItems: false }", "[\"a\",1]", true)]
    [InlineData("{ items: [ { type: string } ], additionalItems: false }", "[\"a\",1]", false)]
    [InlineData("{ prefixItems: [ { type: string } ], items: { type: number } }", "[\"a\",1,2]", true)]
    [InlineData("{ prefixItems: [ { type: string } ], items: { type: number } }", "[\"a\",1,\"b\"]", false)]
    [InlineData("{ uniqueItems: true }", "[1,2,1]", false)]
    [InlineData("{ uniqueItems: true }", "[{\"a\":1},{\"a\":2}]", true)]
    [InlineData("{ contains: { const: 2 } }", "[1,2]", true)]
    [InlineData("{ contains: { const: 3 } }", "[1,2]", false)]
    // numbers: draft 6 bounds, and the draft 4 / OpenAPI 3.0 flags
    [InlineData("{ minimum: 1, maximum: 3 }", "3", true)]
    [InlineData("{ exclusiveMaximum: 3 }", "3", false)]
    [InlineData("{ maximum: 3, exclusiveMaximum: true }", "3", false)]
    [InlineData("{ minimum: 3, exclusiveMinimum: true }", "3.5", true)]
    [InlineData("{ multipleOf: 0.01 }", "9.99", true)]
    [InlineData("{ multipleOf: 0.01 }", "9.999", false)]
    // strings: lengths in characters, pattern searches, formats
    [InlineData("{ maxLength: 2 }", "\"😀😀\"", true)]
    [InlineData("{ minLength: 3 }", "\"ab\"", false)]
    [InlineData("{ pattern: '[0-9]{3}' }", "\"ORD-123-X\"", true)]
    [InlineData("{ pattern: '^[0-9]+$' }", "\"12a\"", false)]
    [InlineData("{ format: date-time }", "\"2026-10-29T10:31:22.27Z\"", true)]
    [InlineData("{ format: date-time }", "\"2026-10-29 10:31\"", false)]
    [InlineData("{ format: date }", "\"2026-02-30\"", false)]
    [InlineData("{ format: date }", "\"2026-02-28\"", true)]
    [InlineData("{ format: time }", "\"10:31:22+02:00\"", true)]
    [InlineData("{ format: email }", "\"ada@example.com\"", true)]
    [InlineData("{ format: email }", "\"ada.example.com\"", false)]
    [InlineData("{ format: uuid }", "\"3f2a1b9c-4d5e-4f60-8a7b-9c0d1e2f3a4b\"", true)]
    [InlineData("{ format: uuid }", "\"3f2a1b9c\"", false)]
    [InlineData("{ format: uri }", "\"https://example.com/a\"", true)]
    [InlineData("{ format: uri }", "\"/a\"", false)]
    [InlineData("{ format: ipv4 }", "\"192.168.1.10\"", true)]
    [InlineData("{ format: ipv4 }", "\"10.1\"", false)]
    [InlineData("{ format: ipv6 }", "\"::1\"", true)]
    [InlineData("{ format: color }", "\"anything\"", true)]           // a format outside the list is not checked
    [InlineData("{ type: string, format: email }", "7", false)]       // format applies to strings; type fails
    [InlineData("{ title: Id, description: An id, example: 1, x-owner: team, readOnly: true }", "7", true)]
    public async Task Keywords(string schema, string json, bool passed)
    {
        var step = await Run($"$.v: {{ schema: {schema} }}", $$"""{"v":{{json}}}""");

        Assert.Equal(passed, step.IsSuccess);
    }

    [Fact]
    public async Task ARefToAnotherFile_AndASchemaThatRefersToItself()
    {
        Write("schemas/tree.json", """
            { "type": "object", "required": ["name"],
              "properties": { "name": { "$ref": "common.yaml#/Name" }, "children": { "type": "array", "items": { "$ref": "#" } } } }
            """);
        Write("schemas/common.yaml", "Name: { type: string, minLength: 1 }\n");

        var valid = await Run("$: { schema: ../schemas/tree.json }", """{"name":"a","children":[{"name":"b","children":[]}]}""");
        var invalid = await Run("$: { schema: ../schemas/tree.json }", """{"name":"a","children":[{"name":""}]}""");

        Assert.True(valid.IsSuccess);
        var problem = Assert.Single(invalid.Assertions);
        Assert.Equal(("$.children[0].name", "schema minLength 1"), (problem.Subject, problem.Condition));
    }

    [Fact]
    public async Task AtMostTwentyProblems_AreListed_ThenACount()
    {
        var step = await Run("$.v: { schema: { items: { type: string } } }", $$"""{"v":[{{string.Join(",", Enumerable.Range(1, 23))}}]}""");

        Assert.Equal(21, step.Assertions.Count);
        Assert.Equal(("$.v[19]", "schema type \"string\""), (step.Assertions[19].Subject, step.Assertions[19].Condition));
        Assert.Equal(("$.v", "schema: 3 more problems"), (step.Assertions[20].Subject, step.Assertions[20].Condition));
    }

    [Fact]
    public async Task AMissingValue_FailsTheCheck()
    {
        var step = await Run("$.v: { schema: { type: string } }", "{}");

        var check = Assert.Single(step.Assertions);
        Assert.Equal(("schema {\"type\":\"string\"}", "(missing)", false), (check.Condition, check.ActualValue, check.Passed));
    }

    [Fact]
    public async Task InsideAny_AndInUntil()
    {
        var any = await Run("$.v: { any: [ { schema: { type: string } }, { schema: { type: integer } } ] }", """{"v":3}""");
        Assert.True(any.IsSuccess);

        var calls = 0;
        var workflow = WorkflowLoader.Parse("""
            base_url: https://api.test
            endpoints:
              item: { path: /item }
            steps:
              - call: item
                until:
                  body: { $: { schema: { required: [done] } } }
                  every: 10ms
                  timeout: 5s
            """, Path.Combine(_root, "workflows", "until.yaml"));
        var server = new FakeServer(_ => new FakeResponse(Body: ++calls < 3 ? "{}" : """{"done":true}"""));
        var report = await new WorkflowEngine(new HttpClient(server)).ExecuteAsync(workflow);

        Assert.True(report.Single().IsSuccess);
        Assert.Equal(3, server.Requests.Count);
    }

    [Theory]
    [InlineData("$: { schema: ../schemas/none.json }", "schema file not found: ../schemas/none.json")]
    [InlineData("$: { schema: ../openapi.yaml#/components/schemas/Usr }", "../openapi.yaml has nothing at #/components/schemas/Usr (did you mean 'User'?)")]
    [InlineData("$: { schema: { type: nubmer } }", "schema#/type: unknown type 'nubmer' (did you mean 'number'?)")]
    [InlineData("$: { schema: { properties: { id: { minimun: 1 } } } }", "schema#/properties/id: unknown keyword 'minimun' in a schema (did you mean 'minimum'?)")]
    [InlineData("$: { schema: { if: { type: string } } }", "schema: 'if' is not supported in schemas yet (docs/workflow-format.md lists the keywords)")]
    [InlineData("$: { schema: { $ref: 'https://example.com/owner.json' } }", "schema#/$ref: $ref to a URL is not supported: https://example.com/owner.json")]
    [InlineData("$: { schema: { $ref: '#/$defs/a' } }", "schema#/$ref: $ref #/$defs/a points into the step: put the schema in a file to use local references")]
    [InlineData("$: { schema: { pattern: '[a-' } }", "schema#/pattern: invalid regular expression '[a-': Unterminated [] set")]
    [InlineData("$: { schema: { required: id } }", "schema#/required: required needs a list of property names")]
    [InlineData("$: { schema: { minLength: -1 } }", "schema#/minLength: minLength must be a whole number, 0 or more")]
    [InlineData("$: { schema: [a] }", "schema needs a file or a mapping, e.g. { schema: ../schemas/user.json }")]
    public void InvalidSchemas_AreReportedBeforeAnyRequest(string check, string message)
    {
        var error = LoadError(check);

        Assert.Matches(@"workflows[\\/]test\.yaml:8:\d+: ", error);
        Assert.EndsWith(message, error);
    }

    [Fact]
    public void AFileThatIsNotValidJson_IsAnError()
    {
        Write("schemas/broken.json", "{ \"type\": \"string\",\n  \"minLength\": }");

        Assert.Contains("../schemas/broken.json is not valid JSON:", LoadError("$: { schema: ../schemas/broken.json }"));
        Assert.EndsWith("(line 2)", LoadError("$: { schema: ../schemas/broken.json }"));
    }

    [Fact]
    public void SchemaOnAHeader_IsAnError()
    {
        var yaml = """
            base_url: https://api.test
            endpoints:
              item: { path: /item }
            steps:
              - call: item
                expect:
                  headers:
                    Content-Type: { schema: { type: string } }
            """;

        var error = Assert.Single(Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.Parse(yaml, Path.Combine(_root, "workflows", "h.yaml"))).Errors);

        Assert.EndsWith("schema works on body values only: headers and cookies are text, use matches", error);
    }
}
