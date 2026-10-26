using System.Text;
using System.Text.Json;

namespace Tinyprobe.Core.Parsing;

/// <summary>
/// <c>tinyprobe schema</c>: a JSON Schema (draft 2020-12) of API files and workflows, for editors (completion and
/// hover) and agents. The keys come from the arrays the loader validates with, so the schema cannot list a key the
/// loader rejects; every key needs a description here. The loader stays the validator: the schema does not know
/// imports, endpoint names, params, placeholders or JSONPath. See docs/specs/0006-completion-and-hover.md.
/// </summary>
public static class FormatSchema
{
    public const string Id = "https://raw.githubusercontent.com/kikutano/tinyprobe/main/docs/schema/tinyprobe.schema.json";

    private static readonly Dictionary<string, string> Descriptions = new()
    {
        // API file and workflow
        ["file.name"] = "Name of the workflow, shown in the report. Default: the file name.",
        ["file.description"] = "What the workflow tests, shown in the report.",
        ["file.import"] = "API files whose endpoints the steps can call, relative to this file: one path or a list.",
        ["file.variables"] = "Default variables of the workflow, `name: value`. Each is resolved once per run and may use the environment, other variables and generated values (`${$uuid}`). The environment file overrides them.",
        ["file.settings"] = "`timeout` (seconds per request) and `max_retries` (retries on network errors and timeouts).",
        ["file.steps"] = "The steps, run in order. A step `call`s an endpoint or `sleep`s; the workflow stops at the first failing step.",
        ["file.base_url"] = "Prepended to every `path` of this file, usually `${baseUrl}` from the environment file.",
        ["file.headers"] = "Headers sent by every endpoint of this file, `Name: value`.",
        ["file.auth"] = "Named sets of headers, such as `bearer: { Authorization: \"Bearer ${token}\" }`. An endpoint picks them with `auth:`.",
        ["file.endpoints"] = "The endpoints, by name. Steps call them like functions: `call: <name>`, with arguments in `with`.",

        // endpoint
        ["endpoint.method"] = "HTTP method. Default: GET.",
        ["endpoint.path"] = "Path, after the file's `base_url`; may contain placeholders, such as `/users/${id}`.",
        ["endpoint.query"] = "Names of the query parameters. Each one is sent only when the step passes it in `with`.",
        ["endpoint.headers"] = "Headers of this endpoint, `Name: value`, after the file's headers and auth sets.",
        ["endpoint.auth"] = "The auth set to send, or a list of them (`[bearer, apikey]`).",
        ["endpoint.body"] = "The request body: a YAML value sent as JSON, a string sent as is, or a single `${...}` placeholder.",
        ["endpoint.params"] = "The arguments of the endpoint, `name: default`, or `name:` for a required one. Steps pass them in `with`; they never come from global variables.",

        // step
        ["step.name"] = "Shown in the report. Default: the call and its path and query arguments.",
        ["step.call"] = "The endpoint to call, by name.",
        ["step.with"] = "Arguments of the endpoint, `name: value`. Values may use variables, such as `id: ${userId}`.",
        ["step.expect"] = "What the response must be: `status`, `headers`, `body` (by JSONPath) and `duration`. Without `status`, any 2xx passes.",
        ["step.until"] = "Polling: repeat the call until these conditions pass, every `every`, at most `timeout`. Then `expect` is checked on the last response.",
        ["step.save"] = "Values of the response for the next steps, `variable: <JSONPath>`, saved after every check passed.",
        ["step.sleep"] = "A step of its own that waits, such as `500ms` or `2s`.",

        // expect and until
        ["expect.status"] = "Expected status code, or a list of accepted ones (`[200, 204]`).",
        ["expect.headers"] = "Checks on response headers, by name (case-insensitive): a value, or conditions such as `{ exists: true }`.",
        ["expect.body"] = "Checks on the JSON body, by JSONPath (`$.id`, `$.items[0].name`): a value, or conditions such as `{ gt: 0 }`.",
        ["expect.duration"] = "Check on the response time, such as `{ lt: 500ms }`.",
        ["until.status"] = "Wait until the status is this code, or one of a list. Without it, any status is accepted while waiting.",
        ["until.headers"] = "Wait until these header checks pass.",
        ["until.body"] = "Wait until these body checks pass, by JSONPath. Stop on every final state, then check the expected one in `expect`.",
        ["until.every"] = "Time between attempts. Default: 1s.",
        ["until.timeout"] = "Give up after this time. Default: 30s.",

        // settings
        ["settings.timeout"] = "Seconds per request. Default: 30.",
        ["settings.max_retries"] = "Retries on network errors and timeouts. HTTP error statuses are never retried. Default: 0.",

        // conditions
        ["condition.eq"] = "Equal to this value.",
        ["condition.ne"] = "Not equal to this value.",
        ["condition.gt"] = "Greater than this number, or this duration in `expect.duration`.",
        ["condition.gte"] = "Greater than or equal to.",
        ["condition.lt"] = "Less than this number, or this duration in `expect.duration`.",
        ["condition.lte"] = "Less than or equal to.",
        ["condition.exists"] = "`true`: the value is present; `false`: it is not.",
        ["condition.contains"] = "A string contains this text, or a list contains this item.",
        ["condition.in"] = "One of these values (OR).",
        ["condition.any"] = "One of these conditions passes (OR), such as `[{ lt: 0 }, { gt: 100 }]`."
    };

    /// <summary>The schema as indented JSON, the text of <c>tinyprobe schema</c> and of docs/schema/tinyprobe.schema.json.</summary>
    public static string Json()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            w.WriteStartObject();
            w.WriteString("$schema", "https://json-schema.org/draft/2020-12/schema");
            w.WriteString("$id", Id);
            w.WriteString("title", "tinyprobe file");
            w.WriteString("description",
                "A tinyprobe API file (endpoints) or workflow (steps). Validate with `tinyprobe inspect`; the format is in `tinyprobe guide`.");
            w.WriteString("type", "object");
            Properties(w, "file", Loader.WorkflowKeys);
            w.WriteBoolean("additionalProperties", false);

            w.WriteStartObject("$defs");
            Definition(w, "endpoint", "An endpoint: what to send.", Loader.EndpointKeys);
            Definition(w, "step", "A step: `call` an endpoint, or `sleep`.", Loader.StepKeys, anyOfRequired: ["call", "sleep"]);
            Definition(w, "expect", "What the response must be.", Loader.ExpectKeys);
            Definition(w, "until", "Polling conditions.", Loader.UntilKeys);
            Definition(w, "settings", "Settings of the workflow.", Loader.SettingsKeys);
            Definition(w, "condition", "Conditions on a value; all must pass.", Loader.Operators.Keys.ToArray());
            Definition(w, "duration_condition", "Conditions on the response time, with durations such as `500ms`.",
                Loader.DurationOperators, section: "condition");

            // A check is conditions (only operators), or a value to compare: anything but an object that mixes
            // operators with other fields, which the loader rejects.
            w.WriteStartObject("check");
            w.WriteString("description", "A value (equal to it), or conditions such as `{ gt: 0, lt: 10 }`.");
            w.WriteStartArray("anyOf");
            Ref(w, "condition");
            w.WriteStartObject();
            w.WriteStartArray("type");
            foreach (var type in new[] { "string", "number", "boolean", "null", "array" })
                w.WriteStringValue(type);
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteStartObject();
            w.WriteString("type", "object");
            w.WriteStartObject("propertyNames");
            w.WriteStartObject("not");
            w.WriteStartArray("enum");
            foreach (var op in Loader.Operators.Keys)
                w.WriteStringValue(op);
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartObject("headers");
            w.WriteString("type", "object");
            w.WriteStartObject("additionalProperties");
            w.WriteStartArray("type");
            foreach (var type in new[] { "string", "number", "boolean" })
                w.WriteStringValue(type);
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void Definition(Utf8JsonWriter w, string name, string description, string[] keys,
        string[]? anyOfRequired = null, string? section = null)
    {
        w.WriteStartObject(name);
        w.WriteString("description", description);
        w.WriteString("type", "object");
        Properties(w, section ?? name, keys);
        w.WriteBoolean("additionalProperties", false);
        if (anyOfRequired is not null)
        {
            w.WriteStartArray("anyOf");
            foreach (var key in anyOfRequired)
            {
                w.WriteStartObject();
                w.WriteStartArray("required");
                w.WriteStringValue(key);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    private static void Properties(Utf8JsonWriter w, string section, string[] keys)
    {
        w.WriteStartObject("properties");
        foreach (var key in keys)
        {
            var id = $"{section}.{key}";
            if (!Descriptions.TryGetValue(id, out var description))
                throw new InvalidOperationException($"FormatSchema has no description for '{id}': add one.");

            w.WriteStartObject(key);
            w.WriteString("description", description);
            WriteType(w, section, key);
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    /// <summary>The type of each key: loose where the loader accepts several forms or placeholders.</summary>
    private static void WriteType(Utf8JsonWriter w, string section, string key)
    {
        switch ($"{section}.{key}")
        {
            case "file.name" or "file.description" or "file.base_url" or "endpoint.path"
                or "step.name" or "step.call" or "step.sleep" or "until.every" or "until.timeout":
                w.WriteString("type", "string");
                break;
            case "endpoint.method":
                w.WriteString("type", "string");
                w.WriteString("pattern", "^[A-Za-z]+$");
                w.WriteStartArray("examples");
                foreach (var method in new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" })
                    w.WriteStringValue(method);
                w.WriteEndArray();
                break;
            case "file.import" or "endpoint.auth":
                StringOrList(w);
                break;
            case "endpoint.query":
                w.WriteString("type", "array");
                w.WriteStartObject("items");
                w.WriteString("type", "string");
                w.WriteEndObject();
                break;
            case "file.headers" or "endpoint.headers":
                Ref(w, "headers", inline: true);
                break;
            case "file.auth":
                MapOf(w, () => Ref(w, "headers", inline: true));
                break;
            case "file.endpoints":
                MapOf(w, () => Ref(w, "endpoint", inline: true));
                break;
            case "file.steps":
                w.WriteString("type", "array");
                w.WriteStartObject("items");
                Ref(w, "step", inline: true);
                w.WriteEndObject();
                break;
            case "file.settings":
                Ref(w, "settings", inline: true);
                break;
            case "file.variables" or "step.with" or "endpoint.params":
                w.WriteString("type", "object");
                break;
            case "step.save":
                MapOf(w, () => w.WriteString("type", "string"));
                break;
            case "step.expect":
                Ref(w, "expect", inline: true);
                break;
            case "step.until":
                Ref(w, "until", inline: true);
                break;
            case "expect.status" or "until.status":
                w.WriteStartArray("anyOf");
                w.WriteStartObject();
                w.WriteString("type", "integer");
                w.WriteEndObject();
                w.WriteStartObject();
                w.WriteString("type", "array");
                w.WriteStartObject("items");
                w.WriteString("type", "integer");
                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndArray();
                break;
            case "expect.headers" or "expect.body" or "until.headers" or "until.body":
                MapOf(w, () => Ref(w, "check", inline: true));
                break;
            case "expect.duration":
                Ref(w, "duration_condition", inline: true);
                break;
            case "settings.timeout":
                w.WriteString("type", "number");
                break;
            case "settings.max_retries":
                w.WriteString("type", "integer");
                break;
            case "condition.exists":
                w.WriteString("type", "boolean");
                break;
            case "condition.in":
                w.WriteString("type", "array");
                break;
            case "condition.any":
                w.WriteString("type", "array");
                w.WriteStartObject("items");
                Ref(w, "check", inline: true);
                w.WriteEndObject();
                break;
            default:
                // endpoint.body, condition.eq/ne/gt/gte/lt/lte/contains: any value.
                break;
        }
    }

    private static void StringOrList(Utf8JsonWriter w)
    {
        w.WriteStartArray("anyOf");
        w.WriteStartObject();
        w.WriteString("type", "string");
        w.WriteEndObject();
        w.WriteStartObject();
        w.WriteString("type", "array");
        w.WriteStartObject("items");
        w.WriteString("type", "string");
        w.WriteEndObject();
        w.WriteEndObject();
        w.WriteEndArray();
    }

    private static void MapOf(Utf8JsonWriter w, Action value)
    {
        w.WriteString("type", "object");
        w.WriteStartObject("additionalProperties");
        value();
        w.WriteEndObject();
    }

    /// <summary>A <c>$ref</c> in its own object, or (inline) as a key of the current one.</summary>
    private static void Ref(Utf8JsonWriter w, string definition, bool inline = false)
    {
        if (!inline)
            w.WriteStartObject();
        w.WriteString("$ref", $"#/$defs/{definition}");
        if (!inline)
            w.WriteEndObject();
    }
}
