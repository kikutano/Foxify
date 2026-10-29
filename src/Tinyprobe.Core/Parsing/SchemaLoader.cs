using System.Text.Json;
using System.Text.RegularExpressions;
using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Execution;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Tinyprobe.Core.Parsing;

/// <summary>
/// Reads the JSON Schemas of <c>schema</c> checks and checks them before any request (docs/specs/0016-json-schema.md):
/// files and pointers that lead nowhere, unknown keywords and types, invalid patterns. Every file a schema reaches
/// through <c>$ref</c> is read too, so a run never reads a schema.
/// </summary>
internal sealed class SchemaLoader
{
    internal static readonly string[] Types = ["string", "number", "integer", "boolean", "object", "array", "null"];

    /// <summary>The keywords the validator checks.</summary>
    internal static readonly string[] Keywords =
    [
        "type", "enum", "const", "nullable", "allOf", "anyOf", "oneOf", "not", "$ref",
        "properties", "required", "additionalProperties", "patternProperties", "minProperties", "maxProperties",
        "items", "prefixItems", "additionalItems", "minItems", "maxItems", "uniqueItems", "contains",
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        "minLength", "maxLength", "pattern", "format"
    ];

    /// <summary>Read and not checked: annotations, and the places that hold schemas for <c>$ref</c>.</summary>
    private static readonly HashSet<string> Ignored =
    [
        "title", "description", "default", "examples", "example", "readOnly", "writeOnly", "deprecated",
        "$schema", "$id", "$comment", "discriminator", "xml", "externalDocs", "contentMediaType", "contentEncoding",
        "$defs", "definitions"
    ];

    /// <summary>Keywords of the standard that this validator does not implement: an error, never a silent pass.</summary>
    private static readonly HashSet<string> Unsupported =
    [
        "if", "then", "else", "dependentRequired", "dependentSchemas", "dependencies", "propertyNames",
        "unevaluatedProperties", "unevaluatedItems", "$dynamicRef", "$dynamicAnchor", "$recursiveRef", "$recursiveAnchor",
        "$anchor", "$vocabulary", "minContains", "maxContains", "contentSchema"
    ];

    private static readonly string[] SchemaKeywords = ["not", "additionalProperties", "additionalItems", "contains"];
    private static readonly string[] SchemaLists = ["allOf", "anyOf", "oneOf", "prefixItems"];
    private static readonly string[] SchemaMaps = ["properties", "patternProperties"];
    private static readonly string[] Counts = ["minProperties", "maxProperties", "minItems", "maxItems", "minLength", "maxLength"];
    private static readonly string[] Numbers = ["minimum", "maximum", "multipleOf"];

    /// <summary>Files read so far, by full path: null when they could not be read (the error is reported once).</summary>
    private readonly Dictionary<string, JsonElement?> _documents = new();

    /// <summary>A schema in a file: <c>path</c> or <c>path#/json/pointer</c>, relative to <paramref name="directory"/>.</summary>
    public SchemaReference? FromFile(string written, string directory, Action<string> error)
    {
        var (path, pointer) = Split(written);
        var fullPath = Path.GetFullPath(Path.Combine(directory, path));
        if (!File.Exists(fullPath))
        {
            error($"schema file not found: {path}");
            return null;
        }
        if (Read(fullPath, path, error) is not { } root)
            return null;
        if (!Navigate(root, pointer, out var schema, out var missing))
        {
            error($"{path} has nothing at #{pointer}{missing}");
            return null;
        }
        return Check(schema, fullPath, path, pointer, written, Path.GetDirectoryName(fullPath)!, error);
    }

    /// <summary>A schema written in the step; its <c>$ref</c> to files are relative to <paramref name="directory"/>.</summary>
    public SchemaReference? Inline(JsonElement schema, string directory, Action<string> error) =>
        Check(schema, null, "schema", "", schema.GetRawText(), directory, error);

    private SchemaReference? Check(JsonElement schema, string? file, string display, string pointer, string written, string directory, Action<string> error)
    {
        var context = new Context(directory, error);
        CheckSchema(schema, file, display, pointer, context);
        if (context.Failed)
            return null;

        return new SchemaReference
        {
            Display = written,
            File = file,
            Schema = schema,
            Documents = _documents.Where(d => d.Value is not null).ToDictionary(d => d.Key, d => d.Value!.Value),
            Patterns = context.Patterns,
            Directory = directory
        };
    }

    private sealed class Context(string directory, Action<string> error)
    {
        public string Directory { get; } = directory;
        public bool Failed { get; private set; }
        public Dictionary<string, Regex> Patterns { get; } = new();
        public HashSet<string> Visited { get; } = new();

        public void Error(string where, string message)
        {
            Failed = true;
            error(where.Length > 0 ? $"{where}: {message}" : message);
        }
    }

    private void CheckSchema(JsonElement schema, string? file, string display, string pointer, Context context)
    {
        // `true` and `false` are schemas too: anything, and nothing.
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return;

        var where = Where(display, pointer);
        if (schema.ValueKind != JsonValueKind.Object)
        {
            context.Error(where, "a schema is a mapping, or true or false");
            return;
        }

        foreach (var keyword in schema.EnumerateObject())
        {
            var name = keyword.Name;
            var value = keyword.Value;
            var at = $"{pointer}/{Escape(name)}";

            if (name.StartsWith("x-", StringComparison.Ordinal) || Ignored.Contains(name))
                continue;
            if (Unsupported.Contains(name))
            {
                context.Error(where, $"'{name}' is not supported in schemas yet (docs/workflow-format.md lists the keywords)");
                continue;
            }
            if (!Keywords.Contains(name))
            {
                context.Error(where, $"unknown keyword '{name}' in a schema{Loader.Suggest(name, Keywords)}");
                continue;
            }

            switch (name)
            {
                case "type":
                    foreach (var type in value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [value])
                    {
                        if (type.ValueKind != JsonValueKind.Null && (type.ValueKind != JsonValueKind.String || !Types.Contains(type.GetString())))
                            context.Error(Where(display, at), $"unknown type {Text(type)}{(type.ValueKind == JsonValueKind.String ? Loader.Suggest(type.GetString()!, Types) : "")}");
                    }
                    break;
                case "enum":
                    if (value.ValueKind != JsonValueKind.Array)
                        context.Error(Where(display, at), "enum needs a list of values");
                    break;
                case "required":
                    if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(n => n.ValueKind != JsonValueKind.String))
                        context.Error(Where(display, at), "required needs a list of property names");
                    break;
                case "nullable" or "uniqueItems":
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        context.Error(Where(display, at), $"{name} must be true or false");
                    break;
                case "exclusiveMinimum" or "exclusiveMaximum":
                    if (value.ValueKind is not (JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
                        context.Error(Where(display, at), $"{name} must be a number, or true or false");
                    break;
                case "pattern":
                    CheckPattern(value, Where(display, at), context);
                    break;
                case "format":
                    if (value.ValueKind != JsonValueKind.String)
                        context.Error(Where(display, at), "format must be a name, such as date-time");
                    break;
                case "items":
                    if (value.ValueKind == JsonValueKind.Array)
                        CheckSchemas(value, file, display, at, context);
                    else
                        CheckSchema(value, file, display, at, context);
                    break;
                case "$ref":
                    CheckReference(value, file, display, at, context);
                    break;
                case "const":
                    break;
                default:
                    if (SchemaKeywords.Contains(name))
                        CheckSchema(value, file, display, at, context);
                    else if (SchemaLists.Contains(name))
                        CheckSchemas(value, file, display, at, context);
                    else if (SchemaMaps.Contains(name))
                        CheckSchemaMap(name, value, file, display, at, context);
                    else if (Counts.Contains(name) && !(value.TryGetInt32(out var count) && count >= 0))
                        context.Error(Where(display, at), $"{name} must be a whole number, 0 or more");
                    else if (Numbers.Contains(name) && (value.ValueKind != JsonValueKind.Number || (name == "multipleOf" && value.GetDouble() <= 0)))
                        context.Error(Where(display, at), name == "multipleOf" ? "multipleOf must be a number greater than 0" : $"{name} must be a number");
                    break;
            }
        }
    }

    private void CheckSchemas(JsonElement list, string? file, string display, string pointer, Context context)
    {
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
        {
            context.Error(Where(display, pointer), "needs a list of schemas");
            return;
        }
        var index = 0;
        foreach (var item in list.EnumerateArray())
            CheckSchema(item, file, display, $"{pointer}/{index++}", context);
    }

    private void CheckSchemaMap(string name, JsonElement map, string? file, string display, string pointer, Context context)
    {
        if (map.ValueKind != JsonValueKind.Object)
        {
            context.Error(Where(display, pointer), $"{name} needs a mapping of names to schemas");
            return;
        }
        foreach (var property in map.EnumerateObject())
        {
            if (name == "patternProperties")
                CheckPattern(property.Name, Where(display, pointer), context);
            CheckSchema(property.Value, file, display, $"{pointer}/{Escape(property.Name)}", context);
        }
    }

    private static void CheckPattern(JsonElement value, string where, Context context)
    {
        if (value.ValueKind != JsonValueKind.String)
            context.Error(where, "pattern must be a regular expression as text");
        else
            CheckPattern(value.GetString()!, where, context);
    }

    private static void CheckPattern(string pattern, string where, Context context)
    {
        if (context.Patterns.ContainsKey(pattern))
            return;
        if (Execution.Patterns.TryCreate(pattern, out var regex, out var reason))
            context.Patterns[pattern] = regex!;
        else
            context.Error(where, $"invalid regular expression '{pattern}': {reason}");
    }

    /// <summary>Follows a <c>$ref</c> once (cycles are fine: a schema may refer to itself), reading its file if needed.</summary>
    private void CheckReference(JsonElement value, string? file, string display, string pointer, Context context)
    {
        var where = Where(display, pointer);
        if (value.ValueKind != JsonValueKind.String)
        {
            context.Error(where, "$ref must be a reference as text, such as #/components/schemas/User");
            return;
        }

        var reference = value.GetString()!;
        if (IsUrl(reference))
        {
            context.Error(where, $"$ref to a URL is not supported: {reference}");
            return;
        }

        var (path, target) = Split(reference);
        string? targetFile;
        string targetDisplay;
        JsonElement root;
        if (path.Length == 0)
        {
            // A local reference, in the file of the schema, or in the schema written in the step.
            targetFile = file;
            targetDisplay = display;
            if (file is null)
            {
                context.Error(where, $"$ref {reference} points into the step: put the schema in a file to use local references");
                return;
            }
            root = _documents[file]!.Value;
        }
        else
        {
            targetFile = Path.GetFullPath(Path.Combine(file is null ? context.Directory : Path.GetDirectoryName(file)!, path));
            targetDisplay = DisplayPath(targetFile);
            if (!File.Exists(targetFile))
            {
                context.Error(where, $"$ref to a file that is not there: {path}");
                return;
            }
            if (Read(targetFile, targetDisplay, message => context.Error("", message)) is not { } read)
                return;
            root = read;
        }

        if (!context.Visited.Add($"{targetFile}#{target}"))
            return;
        if (!Navigate(root, target, out var schema, out var missing))
        {
            context.Error(where, $"$ref {reference}: {targetDisplay} has nothing at #{target}{missing}");
            return;
        }
        CheckSchema(schema, targetFile, targetDisplay, target, context);
    }

    /// <summary>A JSON or YAML file as JSON, read once.</summary>
    private JsonElement? Read(string fullPath, string display, Action<string> error)
    {
        if (_documents.TryGetValue(fullPath, out var cached))
            return cached;

        JsonElement? root = null;
        try
        {
            var text = File.ReadAllText(fullPath);
            if (Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var document = JsonDocument.Parse(text);
                root = document.RootElement.Clone();
            }
            else
            {
                var stream = new YamlStream();
                stream.Load(new StringReader(text));
                root = stream.Documents.Count == 0 ? null : TemplateRenderer.ToLiteral(stream.Documents[0].RootNode);
                if (root is null)
                    error($"{display} is empty");
            }
        }
        catch (JsonException ex)
        {
            error($"{display} is not valid JSON: {ex.Message.Split(" LineNumber:")[0].TrimEnd('.')} (line {ex.LineNumber + 1})");
        }
        catch (YamlException ex)
        {
            error($"{display} is not valid YAML: {ex.Message} (line {ex.Start.Line})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error($"cannot read {display}: {ex.Message}");
        }

        _documents[fullPath] = root;
        return root;
    }

    // --- References ----------------------------------------------------------------

    internal static bool IsUrl(string reference) => reference.Contains("://", StringComparison.Ordinal);

    /// <summary><c>file.json#/a/b</c> into the file and the pointer (<c>/a/b</c>, or empty for the whole file).</summary>
    internal static (string Path, string Pointer) Split(string reference)
    {
        var hash = reference.IndexOf('#');
        return hash < 0 ? (reference, "") : (reference[..hash], Uri.UnescapeDataString(reference[(hash + 1)..]));
    }

    /// <summary>Follows a JSON Pointer (<c>~1</c> is <c>/</c>, <c>~0</c> is <c>~</c>); on failure says which step is missing.</summary>
    internal static bool Navigate(JsonElement root, string pointer, out JsonElement found, out string missing)
    {
        found = root;
        missing = string.Empty;
        if (pointer.Length == 0)
            return true;

        foreach (var raw in pointer.TrimStart('/').Split('/'))
        {
            var token = raw.Replace("~1", "/").Replace("~0", "~");
            if (found.ValueKind == JsonValueKind.Object && found.TryGetProperty(token, out var next))
            {
                found = next;
                continue;
            }
            if (found.ValueKind == JsonValueKind.Array && int.TryParse(token, out var index) && index >= 0 && index < found.GetArrayLength())
            {
                found = found[index];
                continue;
            }
            missing = found.ValueKind == JsonValueKind.Object
                ? Loader.Suggest(token, found.EnumerateObject().Select(p => p.Name))
                : string.Empty;
            return false;
        }
        return true;
    }

    private static string Where(string display, string pointer) => pointer.Length > 0 ? $"{display}#{pointer}" : display;

    private static string Escape(string name) => name.Replace("~", "~0").Replace("/", "~1");

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? $"'{value.GetString()}'" : value.GetRawText();

    private static string DisplayPath(string fullPath)
    {
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? fullPath : relative;
    }
}
