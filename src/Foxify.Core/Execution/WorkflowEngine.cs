using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Foxify.Core.Definitions;
using Foxify.Core.Models;

namespace Foxify.Core.Execution;

/// <summary>
/// Runs a loaded workflow: executes the steps in order, carries saved values from one
/// call to the next and stops at the first failing step.
/// The <see cref="HttpClient"/> is owned by the caller and is not disposed by the engine.
/// </summary>
public sealed class WorkflowEngine
{
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(250);
    private static readonly IReadOnlyDictionary<string, JsonElement> NoVariables = new Dictionary<string, JsonElement>();

    private readonly HttpClient _httpClient;
    private readonly TextWriter _log;

    /// <summary>Raised before each step runs, with a short description of the step.</summary>
    public event Action<string>? StepStarted;

    /// <summary>Raised after each call step, successful or not.</summary>
    public event Action<StepReport>? StepCompleted;

    public WorkflowEngine(HttpClient httpClient, TextWriter? log = null)
    {
        _httpClient = httpClient;
        _log = log ?? TextWriter.Null;
    }

    public async Task<WorkflowReport> ExecuteAsync(
        WorkflowDefinition workflow, IReadOnlyDictionary<string, JsonElement>? environment = null)
    {
        var report = new WorkflowReport { WorkflowName = workflow.Name };
        var globals = new GlobalVariables(workflow.Variables, environment ?? NoVariables);

        _log.WriteLine($"Executing workflow: {workflow.Name}");

        foreach (var step in workflow.Steps)
        {
            if (step is SleepStep sleep)
            {
                var description = $"sleep {Durations.Format(sleep.Duration)}";
                StepStarted?.Invoke(description);
                _log.WriteLine(description);
                await Task.Delay(sleep.Duration);
                continue;
            }

            var call = (CallStep)step;
            var stepReport = new StepReport { StepName = call.Name };
            StepStarted?.Invoke(call.Name);

            await ExecuteCallAsync(call, globals, workflow.Settings, stepReport);

            report.StepReports.Add(stepReport);
            StepCompleted?.Invoke(stepReport);

            if (!stepReport.IsSuccess)
            {
                _log.WriteLine($"Step '{call.Name}' failed. Stopping workflow execution.");
                break;
            }
        }

        return report;
    }

    private async Task ExecuteCallAsync(CallStep step, GlobalVariables globals, WorkflowSettings settings, StepReport report)
    {
        try
        {
            // Arguments are resolved against the globals (e.g. `id: ${userId}`), then become
            // the scope of the endpoint's placeholders.
            var arguments = step.With.ToDictionary(a => a.Key, a => TemplateRenderer.RenderValue(a.Value, globals));
            var scope = new CallScope(step.Endpoint, arguments, globals);
            var polling = Stopwatch.StartNew();

            for (int attempt = 1; ; attempt++)
            {
                report.Attempts = attempt;
                using var response = await SendWithRetriesAsync(step.Endpoint, scope, settings.MaxRetries, report);
                // JSON is UTF-8 by definition (RFC 8259), so the body is decoded as UTF-8.
                var body = await response.Content.ReadAsByteArrayAsync();
                _log.WriteLine($"Response Status: {response.StatusCode} ({report.ExecutionTimeMilliseconds} ms)");
                _log.WriteLine($"Response Content: {Encoding.UTF8.GetString(body)}");

                if (step.Until is { } until && CheckUntil(until, response, body, scope) is { } pending)
                {
                    if (polling.Elapsed + until.Every > until.Timeout)
                    {
                        report.StatusCode = response.StatusCode;
                        report.BytesReceived = body.Length;
                        report.Assertions.AddRange(pending.FailedChecks);
                        var gaveUp = $"Gave up after {attempt} attempts in {Durations.Format(until.Timeout)}";
                        Fail(report, pending.FailedChecks.Count > 0 ? gaveUp : $"{gaveUp}: {pending.Reason}");
                        return;
                    }

                    _log.WriteLine($"Waiting: {pending.Reason}. Next attempt in {Durations.Format(until.Every)}");
                    StepStarted?.Invoke($"{step.Name} (attempt {attempt + 1})");
                    await Task.Delay(until.Every);
                    continue;
                }

                CheckResponse(step, response, body, scope, globals, report);
                return;
            }
        }
        catch (Exception ex)
        {
            // Undefined variables, invalid URLs, network errors: the step fails, the run goes on to the report.
            Fail(report, ex is TaskCanceledException ? "Request timed out" : ex.Message);
        }
    }

    /// <summary>
    /// Sends the request, retrying up to <paramref name="maxRetries"/> times on transient
    /// failures (network errors, timeouts). HTTP error status codes are never retried:
    /// they are a valid response, checked against <c>expect</c>.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetriesAsync(
        EndpointDefinition endpoint, CallScope scope, int maxRetries, StepReport report)
    {
        for (int attempt = 0; ; attempt++)
        {
            // A request message can be sent only once, so it is rebuilt on every attempt.
            using var request = RequestBuilder.Build(endpoint, scope);
            report.Method = request.Method.Method;
            report.Url = request.RequestUri;
            report.BytesSent = request.Content?.Headers.ContentLength ?? 0;

            if (attempt == 0)
                _log.WriteLine($"Executing {request.Method} {request.RequestUri}");

            try
            {
                return await SendTimedAsync(request, report);
            }
            catch (Exception ex) when (attempt < maxRetries && ex is HttpRequestException or TaskCanceledException)
            {
                var delay = RetryBaseDelay * (attempt + 1);
                _log.WriteLine($"{ex.Message} Retrying in {delay.TotalMilliseconds} ms ({attempt + 1}/{maxRetries})");
                await Task.Delay(delay);
            }
        }
    }

    private async Task<HttpResponseMessage> SendTimedAsync(HttpRequestMessage request, StepReport report)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await _httpClient.SendAsync(request);
        }
        finally
        {
            report.Duration = stopwatch.Elapsed;
        }
    }

    private sealed record UntilPending(string Reason, List<AssertionResult> FailedChecks);

    /// <summary>The checks of <c>until</c> on one response: null when they all pass, otherwise what is still missing.</summary>
    private static UntilPending? CheckUntil(RetryUntil until, HttpResponseMessage response, byte[] body, CallScope scope)
    {
        var checks = until.Checks;
        int status = (int)response.StatusCode;
        if (checks.Status.Count > 0 && !checks.Status.Contains(status))
            return new UntilPending($"status {status}, waiting for {string.Join(" or ", checks.Status)}", []);

        var results = EvaluateHeaders(checks.Headers, response, scope);
        if (checks.Body.Count > 0)
        {
            if (TryParseJson(body) is not { } json)
                return new UntilPending("the response body is not valid JSON", results.Where(r => !r.Passed).ToList());
            using (json)
                results.AddRange(EvaluateBody(checks.Body, json, scope));
        }

        var failed = results.Where(r => !r.Passed).ToList();
        return failed.Count == 0
            ? null
            : new UntilPending(string.Join("; ", failed.Select(f => $"{f.Subject} {f.Condition}, actual {f.ActualValue}")), failed);
    }

    private static List<AssertionResult> EvaluateHeaders(IReadOnlyList<Check> checks, HttpResponseMessage response, CallScope scope) =>
        checks.SelectMany(check => check.Conditions.Select(condition =>
            CheckEvaluator.EvaluateHeader(check.Subject, condition, HeaderValue(response, check.Subject), scope))).ToList();

    private static IEnumerable<AssertionResult> EvaluateBody(IReadOnlyList<Check> checks, JsonDocument json, CallScope scope)
    {
        foreach (var check in checks)
        {
            JsonElement? actual = JsonPath.TrySelect(json.RootElement, check.Subject, out var value) ? value : null;
            foreach (var condition in check.Conditions)
                yield return CheckEvaluator.EvaluateBody(check.Subject, condition, actual, scope);
        }
    }

    private static JsonDocument? TryParseJson(byte[] body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks the status code, then evaluates every header, body and duration condition,
    /// and finally stores the <c>save</c> values when everything passed.
    /// </summary>
    private void CheckResponse(
        CallStep step, HttpResponseMessage response, byte[] bodyBytes, CallScope scope, GlobalVariables globals, StepReport report)
    {
        var expect = step.Expect;
        report.StatusCode = response.StatusCode;
        report.BytesReceived = bodyBytes.Length;

        int status = (int)response.StatusCode;
        if (expect.Status.Count > 0)
        {
            report.ExpectedStatusCodes.AddRange(expect.Status.Select(code => (HttpStatusCode)code));
            if (!expect.Status.Contains(status))
            {
                Fail(report, $"Expected status {string.Join(" or ", expect.Status)}, got {status}");
                return;
            }
        }
        else if (status is < 200 or > 299)
        {
            Fail(report, $"Expected a 2xx status, got {status} (set expect.status to expect another one)");
            return;
        }

        report.Assertions.AddRange(EvaluateHeaders(expect.Headers, response, scope));

        JsonDocument? json = null;
        if (expect.Body.Count > 0 || step.Save.Count > 0)
        {
            json = TryParseJson(bodyBytes);
            if (json is null)
            {
                Fail(report, "The response body is not valid JSON");
                return;
            }
        }

        using (json)
        {
            if (json is not null)
                report.Assertions.AddRange(EvaluateBody(expect.Body, json, scope));

            foreach (var condition in expect.Duration)
                report.Assertions.Add(CheckEvaluator.EvaluateDuration(condition, report.Duration));

            if (report.Assertions.Any(a => !a.Passed))
            {
                report.IsSuccess = false;
                foreach (var failed in report.Assertions.Where(a => !a.Passed))
                    _log.WriteLine($"Check failed: {failed.Subject} {failed.Condition}, actual {failed.ActualValue}");
                return;
            }

            foreach (var (name, path) in step.Save)
            {
                if (!JsonPath.TrySelect(json!.RootElement, path, out var value))
                {
                    Fail(report, $"Cannot save '{name}': {path} not found in the response");
                    return;
                }

                globals.Save(name, value.Clone());
                _log.WriteLine($"Saved {name} = {JsonValues.ToDisplay(value)}");
            }
        }
    }

    private static string? HeaderValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : null;

    private void Fail(StepReport report, string reason)
    {
        _log.WriteLine(reason);
        report.IsSuccess = false;
        report.FailureReason = reason;
    }
}
