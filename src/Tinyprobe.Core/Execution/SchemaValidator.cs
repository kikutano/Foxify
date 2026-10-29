using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Parsing;

namespace Tinyprobe.Core.Execution;

/// <summary>
/// Validates a value against a JSON Schema loaded by <see cref="SchemaLoader"/> (docs/specs/0016-json-schema.md):
/// drafts 4 to 2020-12 and the schema objects of OpenAPI 3.0 and 3.1, in one set of rules.
/// </summary>
internal static partial class SchemaValidator
{
    /// <summary>A rule the value breaks: where (a JSONPath), the rule, and the value there (null when it is missing).</summary>
    public readonly record struct Problem(string Path, string Rule, JsonElement? Actual);

    public static List<Problem> Validate(SchemaReference schema, JsonElement value, string path)
    {
        var problems = new List<Problem>();
        new Run(schema).Check(schema.Schema, schema.File, value, path, problems);
        return problems;
    }

    public static bool IsValid(SchemaReference schema, JsonElement value) =>
        new Run(schema).Check(schema.Schema, schema.File, value, "$", problems: null);

    private sealed class Run(SchemaReference loaded)
    {
        /// <summary>
        /// Checks every keyword and adds a problem for each broken rule; with <paramref name="problems"/> null
        /// (inside anyOf, oneOf, not, contains) it only answers, and stops at the first broken rule.
        /// </summary>
        public bool Check(JsonElement schema, string? file, JsonElement value, string path, List<Problem>? problems)
        {
            if (schema.ValueKind == JsonValueKind.True)
                return true;
            if (schema.ValueKind == JsonValueKind.False)
                return Fail(problems, path, "false", value);

            // A broken rule is recorded and the checking goes on; when only the answer is needed, the answer is no.
            var valid = true;
            bool Broken(string rule, string? at = null, JsonElement? actual = null, bool missing = false)
            {
                valid = false;
                problems?.Add(new Problem(at ?? path, rule, missing ? null : actual ?? value));
                return problems is not null;
            }
            bool Keep(bool passed)
            {
                if (!passed)
                    valid = false;
                return passed || problems is not null;
            }

            var nullable = schema.TryGetProperty("nullable", out var n) && n.ValueKind == JsonValueKind.True;
            if (nullable && value.ValueKind == JsonValueKind.Null)
                return true;

            foreach (var keyword in schema.EnumerateObject())
            {
                var rule = keyword.Value;
                switch (keyword.Name)
                {
                    case "$ref":
                        var (target, targetFile) = Resolve(rule.GetString()!, file);
                        if (!Keep(Check(target, targetFile, value, path, problems)))
                            return false;
                        break;

                    case "type":
                        var types = rule.ValueKind == JsonValueKind.Array ? rule.EnumerateArray().Select(TypeName).ToList() : [TypeName(rule)];
                        if (!types.Any(t => IsType(value, t)) && !Broken($"type {TypeText(rule, nullable)}"))
                            return false;
                        break;

                    case "enum":
                        if (!rule.EnumerateArray().Any(e => JsonValues.AreEqual(e, value)) && !Broken($"enum {rule.GetRawText()}"))
                            return false;
                        break;

                    case "const":
                        if (!JsonValues.AreEqual(rule, value) && !Broken($"const {rule.GetRawText()}"))
                            return false;
                        break;

                    case "allOf":
                        foreach (var item in rule.EnumerateArray())
                        {
                            if (!Keep(Check(item, file, value, path, problems)))
                                return false;
                        }
                        break;

                    case "anyOf":
                        if (!rule.EnumerateArray().Any(item => Check(item, file, value, path, null)) && !Broken("anyOf: no alternative matches"))
                            return false;
                        break;

                    case "oneOf":
                        var matching = rule.EnumerateArray().Count(item => Check(item, file, value, path, null));
                        if (matching != 1 && !Broken(matching == 0 ? "oneOf: no alternative matches" : $"oneOf: {matching} alternatives match, not one"))
                            return false;
                        break;

                    case "not":
                        if (Check(rule, file, value, path, null) && !Broken("not"))
                            return false;
                        break;

                    case "required" when value.ValueKind == JsonValueKind.Object:
                        foreach (var name in rule.EnumerateArray())
                        {
                            if (!value.TryGetProperty(name.GetString()!, out _) && !Broken("required", Child(path, name.GetString()!), missing: true))
                                return false;
                        }
                        break;

                    case "properties" when value.ValueKind == JsonValueKind.Object:
                        foreach (var property in rule.EnumerateObject())
                        {
                            if (value.TryGetProperty(property.Name, out var child)
                                && !Keep(Check(property.Value, file, child, Child(path, property.Name), problems)))
                                return false;
                        }
                        break;

                    case "patternProperties" when value.ValueKind == JsonValueKind.Object:
                        foreach (var property in value.EnumerateObject())
                        {
                            foreach (var pattern in rule.EnumerateObject())
                            {
                                if (Search(pattern.Name, property.Name)
                                    && !Keep(Check(pattern.Value, file, property.Value, Child(path, property.Name), problems)))
                                    return false;
                            }
                        }
                        break;

                    case "additionalProperties" when value.ValueKind == JsonValueKind.Object:
                        foreach (var property in value.EnumerateObject())
                        {
                            if (IsDeclared(schema, property.Name))
                                continue;
                            if (rule.ValueKind == JsonValueKind.False)
                            {
                                if (!Broken("additionalProperties false", Child(path, property.Name), property.Value))
                                    return false;
                            }
                            else if (!Keep(Check(rule, file, property.Value, Child(path, property.Name), problems)))
                                return false;
                        }
                        break;

                    case "minProperties" when value.ValueKind == JsonValueKind.Object:
                        if (value.EnumerateObject().Count() < rule.GetInt32() && !Broken($"minProperties {rule.GetInt32()}"))
                            return false;
                        break;

                    case "maxProperties" when value.ValueKind == JsonValueKind.Object:
                        if (value.EnumerateObject().Count() > rule.GetInt32() && !Broken($"maxProperties {rule.GetInt32()}"))
                            return false;
                        break;

                    case "items" when value.ValueKind == JsonValueKind.Array:
                        var tuple = rule.ValueKind == JsonValueKind.Array;
                        var first = tuple ? 0 : schema.TryGetProperty("prefixItems", out var prefix) ? prefix.GetArrayLength() : 0;
                        var index = 0;
                        foreach (var item in value.EnumerateArray())
                        {
                            var itemSchema = tuple ? (index < rule.GetArrayLength() ? rule[index] : (JsonElement?)null) : index >= first ? rule : null;
                            if (itemSchema is { } s && !Keep(Check(s, file, item, $"{path}[{index}]", problems)))
                                return false;
                            index++;
                        }
                        break;

                    case "prefixItems" when value.ValueKind == JsonValueKind.Array:
                        for (var i = 0; i < Math.Min(rule.GetArrayLength(), value.GetArrayLength()); i++)
                        {
                            if (!Keep(Check(rule[i], file, value[i], $"{path}[{i}]", problems)))
                                return false;
                        }
                        break;

                    case "additionalItems" when value.ValueKind == JsonValueKind.Array
                                                 && schema.TryGetProperty("items", out var tupleItems) && tupleItems.ValueKind == JsonValueKind.Array:
                        for (var i = tupleItems.GetArrayLength(); i < value.GetArrayLength(); i++)
                        {
                            if (!Keep(Check(rule, file, value[i], $"{path}[{i}]", problems)))
                                return false;
                        }
                        break;

                    case "minItems" when value.ValueKind == JsonValueKind.Array:
                        if (value.GetArrayLength() < rule.GetInt32() && !Broken($"minItems {rule.GetInt32()}"))
                            return false;
                        break;

                    case "maxItems" when value.ValueKind == JsonValueKind.Array:
                        if (value.GetArrayLength() > rule.GetInt32() && !Broken($"maxItems {rule.GetInt32()}"))
                            return false;
                        break;

                    case "uniqueItems" when value.ValueKind == JsonValueKind.Array && rule.ValueKind == JsonValueKind.True:
                        var items = value.EnumerateArray().ToList();
                        var repeated = Enumerable.Range(0, items.Count).FirstOrDefault(i => items.Take(i).Any(p => JsonValues.AreEqual(p, items[i])), -1);
                        if (repeated >= 0 && !Broken("uniqueItems", $"{path}[{repeated}]", items[repeated]))
                            return false;
                        break;

                    case "contains" when value.ValueKind == JsonValueKind.Array:
                        if (!value.EnumerateArray().Any(item => Check(rule, file, item, path, null)) && !Broken("contains: no item matches"))
                            return false;
                        break;

                    case "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum" when value.ValueKind == JsonValueKind.Number:
                        if (!InRange(keyword.Name, rule, schema, value) && !Broken(RangeText(keyword.Name, rule, schema)))
                            return false;
                        break;

                    case "multipleOf" when value.ValueKind == JsonValueKind.Number:
                        if (!IsMultiple(value, rule) && !Broken($"multipleOf {rule.GetRawText()}"))
                            return false;
                        break;

                    case "minLength" when value.ValueKind == JsonValueKind.String:
                        if (Length(value) < rule.GetInt32() && !Broken($"minLength {rule.GetInt32()}"))
                            return false;
                        break;

                    case "maxLength" when value.ValueKind == JsonValueKind.String:
                        if (Length(value) > rule.GetInt32() && !Broken($"maxLength {rule.GetInt32()}"))
                            return false;
                        break;

                    case "pattern" when value.ValueKind == JsonValueKind.String:
                        if (!Search(rule.GetString()!, value.GetString()!) && !Broken($"pattern {Quote(rule.GetString()!)}"))
                            return false;
                        break;

                    case "format" when value.ValueKind == JsonValueKind.String:
                        if (!HasFormat(rule.GetString()!, value.GetString()!) && !Broken($"format {Quote(rule.GetString()!)}"))
                            return false;
                        break;
                }
            }
            return valid;
        }

        /// <summary>A <c>$ref</c>: in the same file, or in another one relative to the file that has it.</summary>
        private (JsonElement Schema, string? File) Resolve(string reference, string? file)
        {
            var (path, pointer) = SchemaLoader.Split(reference);
            var targetFile = path.Length == 0
                ? file
                : Path.GetFullPath(Path.Combine(file is null ? loaded.Directory : Path.GetDirectoryName(file)!, path));
            // The loader followed every $ref, so the file is read and the pointer leads somewhere.
            SchemaLoader.Navigate(loaded.Documents[targetFile!], pointer, out var schema, out _);
            return (schema, targetFile);
        }

        /// <summary>A property declared by <c>properties</c> or <c>patternProperties</c>, which <c>additionalProperties</c> leaves alone.</summary>
        private bool IsDeclared(JsonElement schema, string name) =>
            (schema.TryGetProperty("properties", out var properties) && properties.TryGetProperty(name, out _))
            || (schema.TryGetProperty("patternProperties", out var patterns) && patterns.EnumerateObject().Any(p => Search(p.Name, name)));

        private bool Search(string pattern, string text)
        {
            // Compiled when the schema was loaded; a pattern takes at most Patterns.Timeout on a value.
            try
            {
                return loaded.Patterns[pattern].IsMatch(text);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new PatternException($"the regular expression '{pattern}' took more than {Durations.Format(Patterns.Timeout)} on this value");
            }
        }
    }

    private static bool Fail(List<Problem>? problems, string path, string rule, JsonElement value)
    {
        problems?.Add(new Problem(path, rule, value));
        return false;
    }

    /// <summary>A type name; in YAML <c>[string, null]</c> has a real null, which means the type <c>"null"</c>.</summary>
    private static string TypeName(JsonElement type) => type.ValueKind == JsonValueKind.Null ? "null" : type.GetString()!;

    private static bool IsType(JsonElement value, string type) => type switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && IsWhole(value),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false
    };

    /// <summary><c>1.0</c> is an integer too, as the standard says.</summary>
    private static bool IsWhole(JsonElement value) =>
        value.TryGetDecimal(out var d) ? decimal.Truncate(d) == d : Math.Floor(value.GetDouble()) == value.GetDouble();

    private static string TypeText(JsonElement rule, bool nullable)
    {
        var text = rule.ValueKind == JsonValueKind.Array ? rule.GetRawText() : Quote(rule.GetString()!);
        return nullable ? $"{text} or null" : text;
    }

    /// <summary>The bounds of draft 6 and later (numbers), and of draft 4 and OpenAPI 3.0 (<c>exclusiveMinimum: true</c> on <c>minimum</c>).</summary>
    private static bool InRange(string keyword, JsonElement rule, JsonElement schema, JsonElement value)
    {
        if (rule.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return true; // a flag of minimum or maximum, checked there
        var actual = value.GetDouble();
        var limit = rule.GetDouble();
        return keyword switch
        {
            "minimum" => Exclusive(schema, "exclusiveMinimum") ? actual > limit : actual >= limit,
            "maximum" => Exclusive(schema, "exclusiveMaximum") ? actual < limit : actual <= limit,
            "exclusiveMinimum" => actual > limit,
            _ => actual < limit
        };
    }

    private static bool Exclusive(JsonElement schema, string flag) =>
        schema.TryGetProperty(flag, out var value) && value.ValueKind == JsonValueKind.True;

    private static string RangeText(string keyword, JsonElement rule, JsonElement schema) => keyword switch
    {
        "minimum" when Exclusive(schema, "exclusiveMinimum") => $"exclusiveMinimum {rule.GetRawText()}",
        "maximum" when Exclusive(schema, "exclusiveMaximum") => $"exclusiveMaximum {rule.GetRawText()}",
        _ => $"{keyword} {rule.GetRawText()}"
    };

    private static bool IsMultiple(JsonElement value, JsonElement divisor)
    {
        if (value.TryGetDecimal(out var a) && divisor.TryGetDecimal(out var b))
            return a % b == 0;
        var quotient = value.GetDouble() / divisor.GetDouble();
        return Math.Abs(quotient - Math.Round(quotient)) < 1e-9;
    }

    /// <summary>In characters (code points), as the standard counts them: an emoji is one.</summary>
    private static int Length(JsonElement value) => value.GetString()!.EnumerateRunes().Count();

    private static string Quote(string text) => JsonValues.ToDisplay(JsonValues.FromString(text));

    /// <summary>The formats people use in OpenAPI documents; any other format is not checked.</summary>
    private static bool HasFormat(string format, string text) => format switch
    {
        "date-time" => DateTimePattern().IsMatch(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "date" => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "time" => TimePattern().IsMatch(text) && TimeOnly.TryParseExact(text[..8], "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "email" => EmailPattern().IsMatch(text),
        "uuid" => Guid.TryParseExact(text, "D", out _),
        // A scheme first: .NET also takes "/a" for an absolute (file) URI.
        "uri" => UriScheme().IsMatch(text) && Uri.TryCreate(text, UriKind.Absolute, out _),
        "ipv4" => IPAddress.TryParse(text, out var v4) && v4.AddressFamily == AddressFamily.InterNetwork && text.Count(c => c == '.') == 3,
        "ipv6" => text.Contains(':') && IPAddress.TryParse(text, out var v6) && v6.AddressFamily == AddressFamily.InterNetworkV6,
        _ => true
    };

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}[Tt ][0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})$")]
    private static partial Regex DateTimePattern();

    [GeneratedRegex(@"^[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?([Zz]|[+-][0-9]{2}:[0-9]{2})?$")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex UriScheme();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    /// <summary>A property of a JSONPath: <c>$.city</c>, or <c>$['first name']</c> for a name the subset cannot write.</summary>
    private static string Child(string path, string name) =>
        SimpleName().IsMatch(name) ? $"{path}.{name}" : $"{path}['{name.Replace("'", "\\'")}']";

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_-]*$")]
    private static partial Regex SimpleName();
}
