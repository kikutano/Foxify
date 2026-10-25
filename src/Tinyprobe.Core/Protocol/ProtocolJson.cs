using System.Text;
using System.Text.Json;
using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Models;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Reporting;

namespace Tinyprobe.Core.Protocol;

/// <summary>
/// The JSON of <c>tinyprobe inspect --json</c> and the events of <c>--reporter ndjson</c>, read by tools
/// such as a UI. Keys are snake_case; durations are milliseconds; YAML values are shown as JSON with
/// their placeholders unresolved (<c>"${email}"</c>). <see cref="Version"/> changes only when a key is
/// removed or changes meaning: new keys may appear at any time.
/// </summary>
public static class ProtocolJson
{
    public const int Version = 1;

    // --- inspect ---------------------------------------------------------------

    /// <summary>A valid workflow or API file: its endpoints, and for a workflow its variables, settings and steps.</summary>
    public static string Inspection(string version, WorkflowDefinition workflow, bool indented = true) => Write(indented, w =>
    {
        WriteHeader(w, version);
        w.WriteBoolean("valid", true);
        w.WriteString("kind", workflow.Steps.Count > 0 ? "workflow" : "api");
        w.WriteString("file", workflow.SourceFile);
        w.WriteString("name", workflow.Name);
        w.WriteString("description", workflow.Description);

        w.WriteStartObject("settings");
        w.WriteNumber("timeout_ms", workflow.Settings.Timeout * 1000);
        w.WriteNumber("max_retries", workflow.Settings.MaxRetries);
        w.WriteEndObject();

        w.WriteStartArray("variables");
        foreach (var (name, value) in workflow.Variables)
        {
            w.WriteStartObject();
            w.WriteString("name", name);
            WriteLiteral(w, "value", value);
            WriteLocation(w, value.Location);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("endpoints");
        foreach (var endpoint in workflow.Endpoints.Values)
            WriteEndpoint(w, endpoint);
        w.WriteEndArray();

        w.WriteStartArray("steps");
        for (int index = 0; index < workflow.Steps.Count; index++)
            WriteStep(w, index, workflow.Steps[index]);
        w.WriteEndArray();
    });

    /// <summary>The problems of an invalid file; as a run event, <paramref name="eventName"/> is <c>invalid</c>.</summary>
    public static string Invalid(string version, string file, IReadOnlyList<LoadProblem> problems,
        string? eventName = null, bool indented = true) => Write(indented, w =>
    {
        if (eventName is not null)
            w.WriteString("event", eventName);
        WriteHeader(w, version);
        w.WriteBoolean("valid", false);
        w.WriteString("file", Path.GetFullPath(file));
        w.WriteStartArray("errors");
        foreach (var problem in problems)
        {
            w.WriteStartObject();
            w.WriteString("file", problem.File);
            w.WriteNumber("line", problem.Line);
            w.WriteNumber("column", problem.Column);
            w.WriteString("message", problem.Message);
            w.WriteString("text", problem.ToString());
            w.WriteEndObject();
        }
        w.WriteEndArray();
    });

    private static void WriteEndpoint(Utf8JsonWriter w, EndpointDefinition endpoint)
    {
        w.WriteStartObject();
        w.WriteString("name", endpoint.Name);
        w.WriteString("method", endpoint.Method);
        if (endpoint.BaseUrl is null)
            w.WriteNull("base_url");
        else
            w.WriteString("base_url", endpoint.BaseUrl.ToString());
        w.WriteString("path", endpoint.Path.ToString());

        w.WriteStartArray("query");
        foreach (var name in endpoint.Query)
            w.WriteStringValue(name);
        w.WriteEndArray();

        // Effective headers: a later one with the same name replaces an earlier one, as in the request.
        var headers = new Dictionary<string, KeyValuePair<string, Template>>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in endpoint.Headers)
            headers[header.Key] = header;
        w.WriteStartArray("headers");
        foreach (var (name, value) in headers.Values)
        {
            w.WriteStartObject();
            w.WriteString("name", name);
            w.WriteString("value", value.ToString());
            w.WriteEndObject();
        }
        w.WriteEndArray();

        if (endpoint.Body is null)
            w.WriteNull("body");
        else
            WriteLiteral(w, "body", endpoint.Body);

        w.WriteStartArray("params");
        foreach (var (name, defaultValue) in endpoint.Params ?? new Dictionary<string, Template?>())
        {
            w.WriteStartObject();
            w.WriteString("name", name);
            w.WriteBoolean("required", defaultValue is null);
            if (defaultValue is null)
                w.WriteNull("default");
            else
                WriteLiteral(w, "default", defaultValue);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("arguments");
        foreach (var name in Loader.EndpointArguments(endpoint))
            w.WriteStringValue(name);
        w.WriteEndArray();

        WriteLocation(w, endpoint.Location);
        w.WriteEndObject();
    }

    private static void WriteStep(Utf8JsonWriter w, int index, StepDefinition step)
    {
        w.WriteStartObject();
        w.WriteNumber("index", index);

        if (step is SleepStep sleep)
        {
            w.WriteString("type", "sleep");
            w.WriteString("name", sleep.Name);
            w.WriteNumber("duration_ms", sleep.Duration.TotalMilliseconds);
            WriteLocation(w, step.Location);
            w.WriteEndObject();
            return;
        }

        var call = (CallStep)step;
        w.WriteString("type", "call");
        w.WriteString("name", call.Name);
        w.WriteString("call", call.Call);

        w.WriteStartObject("with");
        foreach (var (name, value) in call.With)
            WriteLiteral(w, name, value);
        w.WriteEndObject();

        w.WritePropertyName("expect");
        WriteChecks(w, call.Expect);

        if (call.Until is { } until)
        {
            w.WritePropertyName("until");
            WriteChecks(w, until.Checks, until);
        }
        else
        {
            w.WriteNull("until");
        }

        w.WriteStartObject("save");
        foreach (var (name, path) in call.Save)
            w.WriteString(name, path);
        w.WriteEndObject();

        WriteLocation(w, step.Location);
        w.WriteEndObject();
    }

    private static void WriteChecks(Utf8JsonWriter w, Expectation expectation, RetryUntil? until = null)
    {
        w.WriteStartObject();
        w.WriteStartArray("status");
        foreach (var code in expectation.Status)
            w.WriteNumberValue(code);
        w.WriteEndArray();

        w.WriteStartArray("checks");
        foreach (var check in expectation.Headers)
            WriteConditions(w, $"header {check.Subject}", check.Conditions);
        foreach (var check in expectation.Body)
            WriteConditions(w, check.Subject, check.Conditions);
        WriteConditions(w, "duration", expectation.Duration);
        w.WriteEndArray();

        if (until is not null)
        {
            w.WriteNumber("every_ms", until.Every.TotalMilliseconds);
            w.WriteNumber("timeout_ms", until.Timeout.TotalMilliseconds);
        }
        w.WriteEndObject();
    }

    private static void WriteConditions(Utf8JsonWriter w, string subject, IReadOnlyList<Condition> conditions)
    {
        foreach (var condition in conditions)
        {
            w.WriteStartObject();
            w.WriteString("subject", subject);
            w.WriteString("condition", CheckEvaluator.Describe(condition, scope: null));
            w.WriteEndObject();
        }
    }

    // --- run events ------------------------------------------------------------

    public static string RunStarted(string version, WorkflowDefinition workflow, string? environmentFile) => Write(false, w =>
    {
        w.WriteString("event", "run_started");
        WriteHeader(w, version);
        w.WriteString("workflow", workflow.Name);
        w.WriteString("file", workflow.SourceFile);
        if (environmentFile is null)
            w.WriteNull("env_file");
        else
            w.WriteString("env_file", Path.GetFullPath(environmentFile));

        w.WriteStartArray("steps");
        for (int index = 0; index < workflow.Steps.Count; index++)
        {
            var step = workflow.Steps[index];
            w.WriteStartObject();
            w.WriteNumber("index", index);
            w.WriteString("type", step is SleepStep ? "sleep" : "call");
            w.WriteString("name", step is CallStep call ? call.Name : ((SleepStep)step).Name);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    });

    public static string StepStarted(StepProgress progress) => Write(false, w =>
    {
        w.WriteString("event", "step_started");
        w.WriteNumber("index", progress.Index);
        w.WriteString("type", progress.IsSleep ? "sleep" : "call");
        w.WriteString("name", progress.Name);
        w.WriteNumber("attempt", progress.Attempt);
    });

    public static string StepCompleted(StepReport step) => Write(false, w =>
    {
        w.WriteString("event", "step_completed");
        w.WriteNumber("index", step.Index);
        w.WriteString("name", step.StepName);
        w.WriteBoolean("success", step.IsSuccess);
        WriteNullableString(w, "failure", step.FailureReason);
        WriteNullableString(w, "method", step.Method);
        WriteNullableString(w, "url", step.Url?.ToString());
        if (step.StatusCode is { } status)
            w.WriteNumber("status", (int)status);
        else
            w.WriteNull("status");

        w.WriteStartArray("expected_status");
        foreach (var code in step.ExpectedStatusCodes)
            w.WriteNumberValue((int)code);
        w.WriteEndArray();

        w.WriteNumber("duration_ms", Milliseconds(step.Duration));
        w.WriteNumber("attempts", step.Attempts);
        w.WriteNumber("bytes_sent", step.BytesSent);
        w.WriteNumber("bytes_received", step.BytesReceived);

        w.WriteStartArray("assertions");
        foreach (var assertion in step.Assertions)
        {
            w.WriteStartObject();
            w.WriteString("subject", assertion.Subject);
            w.WriteString("condition", assertion.Condition);
            w.WriteString("actual", assertion.ActualValue);
            w.WriteBoolean("passed", assertion.Passed);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartObject("saved");
        foreach (var (name, value) in step.Saved)
        {
            w.WritePropertyName(name);
            value.WriteTo(w);
        }
        w.WriteEndObject();

        if (step.Request is { } request)
        {
            w.WriteStartObject("request");
            w.WriteString("method", request.Method);
            w.WriteString("url", request.Url);
            WriteHeaders(w, request.Headers);
            WriteNullableString(w, "body", request.Body);
            w.WriteEndObject();
        }
        else
        {
            w.WriteNull("request");
        }

        if (step.Response is { } response)
        {
            w.WriteStartObject("response");
            w.WriteNumber("status", response.Status);
            WriteNullableString(w, "reason", response.Reason);
            WriteHeaders(w, response.Headers);
            w.WriteString("body", response.Body);
            w.WriteBoolean("body_truncated", response.BodyTruncated);
            w.WriteEndObject();
        }
        else
        {
            w.WriteNull("response");
        }
    });

    public static string RunFinished(WorkflowSummary summary, TimeSpan elapsed) => Write(false, w =>
    {
        w.WriteString("event", "run_finished");
        w.WriteBoolean("success", summary.IsSuccess);
        w.WriteStartObject("summary");
        w.WriteNumber("total_steps", summary.TotalSteps);
        w.WriteNumber("passed_steps", summary.PassedSteps);
        w.WriteNumber("failed_steps", summary.FailedSteps);
        w.WriteNumber("not_run_steps", summary.NotRunSteps);
        w.WriteNumber("checks_passed", summary.ChecksPassed);
        w.WriteNumber("checks_failed", summary.ChecksFailed);
        w.WriteNumber("http_requests", summary.HttpRequests);
        w.WriteNumber("bytes_sent", summary.BytesSent);
        w.WriteNumber("bytes_received", summary.BytesReceived);
        w.WriteNumber("duration_ms", Milliseconds(elapsed));

        if (summary.HttpRequestDuration is { } d)
        {
            w.WriteStartObject("http_req_duration_ms");
            w.WriteNumber("avg", Milliseconds(d.Average));
            w.WriteNumber("min", Milliseconds(d.Min));
            w.WriteNumber("med", Milliseconds(d.Median));
            w.WriteNumber("max", Milliseconds(d.Max));
            w.WriteNumber("p90", Milliseconds(d.P90));
            w.WriteNumber("p95", Milliseconds(d.P95));
            w.WriteEndObject();
        }
        else
        {
            w.WriteNull("http_req_duration_ms");
        }
        w.WriteEndObject();
    });

    // --- helpers ---------------------------------------------------------------

    private static void WriteHeader(Utf8JsonWriter w, string version)
    {
        w.WriteNumber("protocol", Version);
        w.WriteString("version", version);
    }

    private static void WriteLiteral(Utf8JsonWriter w, string name, Template template)
    {
        w.WritePropertyName(name);
        TemplateRenderer.ToLiteral(template.Node).WriteTo(w);
    }

    private static void WriteLocation(Utf8JsonWriter w, SourceLocation location)
    {
        w.WriteStartObject("location");
        w.WriteString("file", location.File);
        w.WriteNumber("line", location.Line);
        w.WriteNumber("column", location.Column);
        w.WriteEndObject();
    }

    private static void WriteHeaders(Utf8JsonWriter w, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        w.WriteStartArray("headers");
        foreach (var (name, value) in headers)
        {
            w.WriteStartObject();
            w.WriteString("name", name);
            w.WriteString("value", value);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteNullableString(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null)
            w.WriteNull(name);
        else
            w.WriteString(name, value);
    }

    private static double Milliseconds(TimeSpan duration) => Math.Round(duration.TotalMilliseconds, 3);

    private static string Write(bool indented, Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
