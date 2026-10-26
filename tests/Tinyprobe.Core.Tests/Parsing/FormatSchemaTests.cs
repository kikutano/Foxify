using System.Text.Json;
using System.Text.RegularExpressions;
using Tinyprobe.Core.Parsing;

namespace Tinyprobe.Core.Tests.Parsing;

public sealed class FormatSchemaTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tinyprobe-schema-").FullName;
    private static readonly JsonElement Schema = JsonDocument.Parse(FormatSchema.Json()).RootElement.Clone();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tinyprobe.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, relativePath);
    }

    private static string[] Keys(JsonElement schema) =>
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();

    private static JsonElement Definition(string name) => Schema.GetProperty("$defs").GetProperty(name);

    [Fact]
    public void Keys_AreExactlyThoseTheLoaderAccepts_EachWithADescription()
    {
        Assert.Equal(Loader.WorkflowKeys, Keys(Schema));
        Assert.Equal(Loader.EndpointKeys, Keys(Definition("endpoint")));
        Assert.Equal(Loader.StepKeys, Keys(Definition("step")));
        Assert.Equal(Loader.ExpectKeys, Keys(Definition("expect")));
        Assert.Equal(Loader.UntilKeys, Keys(Definition("until")));
        Assert.Equal(Loader.SettingsKeys, Keys(Definition("settings")));
        Assert.Equal(Loader.Operators.Keys, Keys(Definition("condition")));
        Assert.Equal(Loader.DurationOperators, Keys(Definition("duration_condition")));

        foreach (var section in new[] { Schema, Definition("endpoint"), Definition("step"), Definition("condition") })
        {
            Assert.All(section.GetProperty("properties").EnumerateObject(),
                p => Assert.False(string.IsNullOrWhiteSpace(p.Value.GetProperty("description").GetString()), p.Name));
        }
    }

    [Fact]
    public void CommittedSchema_IsWhatTheBinaryPrints()
    {
        // docs/schema/tinyprobe.schema.json is what editors download: regenerate it with `tinyprobe schema`.
        var committed = File.ReadAllText(RepositoryFile("docs/schema/tinyprobe.schema.json")).ReplaceLineEndings("\n");
        Assert.Equal(FormatSchema.Json(), committed);
    }

    [Theory]
    [InlineData("docs/example")]
    [InlineData("example")]
    public void ExampleFiles_MatchTheSchema(string folder)
    {
        var files = Directory.GetFiles(RepositoryFile(Path.Combine(folder, "api")), "*.yaml")
            .Concat(Directory.GetFiles(RepositoryFile(Path.Combine(folder, "workflows")), "*.yaml"))
            .ToList();

        Assert.NotEmpty(files);
        foreach (var file in files)
            Assert.Empty(SchemaValidator.Validate(Schema, WorkflowLoader.ParseYamlValue(File.ReadAllText(file))).Select(e => $"{file}: {e}"));
    }

    [Fact]
    public void Misspelled_Keys_DoNotMatchTheSchema()
    {
        var file = WorkflowLoader.ParseYamlValue("""
            name: Typos
            steps:
              - call: login
                expcet:
                  status: 200
              - call: login
                expect:
                  body:
                    $.id: { gte: 1, betwen: 2 }
              - name: no call
            """);

        var errors = SchemaValidator.Validate(Schema, file);

        Assert.Contains("$.steps[0]: unknown key 'expcet'", errors);
        Assert.Contains(errors, e => e.StartsWith("$.steps[1].expect.body.$.id", StringComparison.Ordinal));
        Assert.Contains("$.steps[2]: matches none of anyOf", errors);
    }

    [Fact]
    public void InvalidWorkflow_StillHasTheEndpointsOfItsImports()
    {
        File.WriteAllText(Path.Combine(_directory, "api.yaml"), "endpoints:\n  get_user:\n    path: /users/${id}\n    params:\n      id:\n");
        var workflow = Path.Combine(_directory, "w.yaml");
        File.WriteAllText(workflow, "import: [api.yaml]\nsteps:\n  - call: get_usr\n");

        var ex = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.LoadAny(workflow));

        Assert.NotNull(ex.Endpoints);
        Assert.Equal(["get_user"], ex.Endpoints.Keys);
    }

    [Fact]
    public void WorkflowWithABrokenImport_HasNoEndpoints()
    {
        File.WriteAllText(Path.Combine(_directory, "api.yaml"), "endpoints:\n  get_user: { pth: /users }\n");
        var workflow = Path.Combine(_directory, "w.yaml");
        File.WriteAllText(workflow, "import: [api.yaml, missing.yaml]\nsteps:\n  - call: get_user\n");

        var ex = Assert.Throws<WorkflowLoadException>(() => WorkflowLoader.LoadAny(workflow));

        Assert.Null(ex.Endpoints);
    }
}

/// <summary>
/// The subset of JSON Schema that FormatSchema uses: type, properties, additionalProperties, required, anyOf,
/// items, pattern, propertyNames, not, enum and local $ref. Enough to check that real files match the schema editors receive.
/// </summary>
internal static class SchemaValidator
{
    public static List<string> Validate(JsonElement root, JsonElement value)
    {
        var errors = new List<string>();
        Check(root, root, value, "$", errors);
        return errors;
    }

    private static void Check(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        if (schema.TryGetProperty("$ref", out var reference))
            Check(root, Resolve(root, reference.GetString()!), value, path, errors);

        if (schema.TryGetProperty("type", out var type) && !HasType(type, value))
            errors.Add($"{path}: expected {type.GetRawText()}, got {value.ValueKind}");

        if (schema.TryGetProperty("pattern", out var pattern) && value.ValueKind == JsonValueKind.String
            && !Regex.IsMatch(value.GetString()!, pattern.GetString()!))
            errors.Add($"{path}: does not match {pattern.GetString()}");

        if (schema.TryGetProperty("anyOf", out var anyOf)
            && !anyOf.EnumerateArray().Any(branch => Validate(root, branch, value, path).Count == 0))
            errors.Add($"{path}: matches none of anyOf");

        if (schema.TryGetProperty("enum", out var values) && !values.EnumerateArray().Any(v => JsonElement.DeepEquals(v, value)))
            errors.Add($"{path}: not one of {values.GetRawText()}");

        if (schema.TryGetProperty("not", out var not) && Validate(root, not, value, path).Count == 0)
            errors.Add($"{path}: matches a schema it must not match");

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("propertyNames", out var names))
            {
                foreach (var property in value.EnumerateObject())
                {
                    using var name = JsonDocument.Parse(JsonSerializer.Serialize(property.Name));
                    Check(root, names, name.RootElement, $"{path}.{property.Name} (name)", errors);
                }
            }

            var properties = schema.TryGetProperty("properties", out var p) ? p : default;
            foreach (var property in value.EnumerateObject())
            {
                var childPath = $"{path}.{property.Name}";
                if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var child))
                    Check(root, child, property.Value, childPath, errors);
                else if (schema.TryGetProperty("additionalProperties", out var additional))
                {
                    if (additional.ValueKind == JsonValueKind.False)
                        errors.Add($"{path}: unknown key '{property.Name}'");
                    else if (additional.ValueKind == JsonValueKind.Object)
                        Check(root, additional, property.Value, childPath, errors);
                }
            }

            if (schema.TryGetProperty("required", out var required))
            {
                foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
                {
                    if (!value.TryGetProperty(name, out _))
                        errors.Add($"{path}: missing '{name}'");
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            int index = 0;
            foreach (var item in value.EnumerateArray())
                Check(root, items, item, $"{path}[{index++}]", errors);
        }
    }

    private static List<string> Validate(JsonElement root, JsonElement schema, JsonElement value, string path)
    {
        var errors = new List<string>();
        Check(root, schema, value, path, errors);
        return errors;
    }

    private static JsonElement Resolve(JsonElement root, string reference)
    {
        Assert.StartsWith("#/$defs/", reference);
        return root.GetProperty("$defs").GetProperty(reference["#/$defs/".Length..]);
    }

    private static bool HasType(JsonElement type, JsonElement value) =>
        type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Any(t => HasType(t, value))
            : type.GetString() switch
            {
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                "string" => value.ValueKind == JsonValueKind.String,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "number" => value.ValueKind == JsonValueKind.Number,
                "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
                "null" => value.ValueKind == JsonValueKind.Null,
                var other => throw new NotSupportedException($"type '{other}'")
            };
}
