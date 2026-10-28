using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Models;

namespace Tinyprobe.Core.Execution;

/// <summary>A step is starting: <see cref="Attempt"/> is more than 1 when an <c>until</c> step polls again.</summary>
public sealed record StepProgress(int Index, string Name, int Attempt = 1, bool IsSleep = false)
{
    /// <summary>The name, plus the attempt when polling: <c>Wait (attempt 2)</c>.</summary>
    public string Description => Attempt > 1 ? $"{Name} (attempt {Attempt})" : Name;
}

/// <summary>
/// Runs a loaded workflow: executes the steps in order, carries saved values from one
/// call to the next and stops at the first failing step.
/// The <see cref="HttpClient"/> is owned by the caller and is not disposed by the engine.
/// Use one engine, client and cookie jar per workflow run, so every run starts with no cookies.
/// </summary>
public sealed class WorkflowEngine
{
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(250);
    private static readonly IReadOnlyDictionary<string, JsonElement> NoVariables = new Dictionary<string, JsonElement>();

    /// <summary>Recorded response bodies are cut after this many bytes.</summary>
    public const int MaxRecordedBodyBytes = 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly TextWriter _log;
    private readonly CookieContainer _cookies;
    private OAuth2Tokens? _tokens;

    /// <summary>Raised before each step runs, and again before each new attempt of an <c>until</c> step.</summary>
    public event Action<StepProgress>? StepStarted;

    /// <summary>Raised after each call step, successful or not.</summary>
    public event Action<StepReport>? StepCompleted;

    /// <param name="cookies">
    /// The jar of the client's handler (<see cref="HttpClientHandler.CookieContainer"/>). The handler stores and sends
    /// the cookies, redirects included; the engine reads the jar for cookie checks, <c>save</c> and the recorded request.
    /// </param>
    public WorkflowEngine(HttpClient httpClient, TextWriter? log = null, CookieContainer? cookies = null)
    {
        _httpClient = httpClient;
        _log = log ?? TextWriter.Null;
        _cookies = cookies ?? new CookieContainer();
    }

    /// <summary>Record the request and response of every call in <see cref="StepReport.Request"/> and <see cref="StepReport.Response"/>.</summary>
    public bool RecordExchanges { get; set; }

    /// <summary>When set, multipart bodies may send only files inside this directory: set by the MCP server, for its workspace.</summary>
    public string? FileRoot { get; set; }

    /// <summary>The clock of OAuth2 token expiry; tests replace it.</summary>
    internal TimeProvider Time { get; init; } = TimeProvider.System;

    public async Task<WorkflowReport> ExecuteAsync(
        WorkflowDefinition workflow, IReadOnlyDictionary<string, JsonElement>? environment = null)
    {
        var report = new WorkflowReport { WorkflowName = workflow.Name };
        var globals = new GlobalVariables(workflow.Variables, environment ?? NoVariables);
        // Files are relative to the file tinyprobe was given, as imports are: the workflow, or the API file of a call.
        var files = new FileAccess(
            string.IsNullOrEmpty(workflow.SourceFile) ? Environment.CurrentDirectory : Path.GetDirectoryName(workflow.SourceFile)!,
            FileRoot);

        _log.WriteLine($"Executing workflow: {workflow.Name}");

        for (int index = 0; index < workflow.Steps.Count; index++)
        {
            if (workflow.Steps[index] is SleepStep sleep)
            {
                StepStarted?.Invoke(new StepProgress(index, sleep.Name, IsSleep: true));
                _log.WriteLine(sleep.Name);
                await Task.Delay(sleep.Duration);
                continue;
            }

            var call = (CallStep)workflow.Steps[index];
            var stepReport = new StepReport { Index = index, StepName = call.Name };
            StepStarted?.Invoke(new StepProgress(index, call.Name));

            await ExecuteCallAsync(call, globals, workflow.Settings, files, stepReport);

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

    private async Task ExecuteCallAsync(
        CallStep step, GlobalVariables globals, WorkflowSettings settings, FileAccess files, StepReport report)
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
                using var response = await SendWithRetriesAsync(step.Endpoint, scope, files, settings.MaxRetries, report);
                // JSON is UTF-8 by definition (RFC 8259), so the body is decoded as UTF-8.
                var body = await response.Content.ReadAsByteArrayAsync();
                _log.WriteLine($"Response Status: {response.StatusCode} ({report.ExecutionTimeMilliseconds} ms)");
                _log.WriteLine($"Response Content: {Encoding.UTF8.GetString(body)}");
                if (RecordExchanges)
                    report.Response = Record(response, body);

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
                    StepStarted?.Invoke(new StepProgress(report.Index, step.Name, attempt + 1));
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
        EndpointDefinition endpoint, CallScope scope, FileAccess files, int maxRetries, StepReport report)
    {
        for (int attempt = 0; ; attempt++)
        {
            // A request message can be sent only once, so it is rebuilt on every attempt.
            var authorization = await AuthorizationAsync(endpoint, scope, maxRetries);
            using var request = RequestBuilder.Build(endpoint, scope, files, authorization);
            report.Method = request.Method.Method;
            report.Url = request.RequestUri;
            report.BytesSent = request.Content?.Headers.ContentLength ?? 0;

            if (attempt == 0)
                _log.WriteLine($"Executing {request.Method} {request.RequestUri}");
            if (RecordExchanges)
            {
                report.Request = await RecordAsync(request, _cookies);
                report.Response = null; // a network error must not leave the previous attempt's response
            }

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

    /// <summary>The <c>Authorization</c> header of the endpoint's <c>basic</c> or <c>oauth2</c> auth set; null without one.</summary>
    private async Task<string?> AuthorizationAsync(EndpointDefinition endpoint, CallScope scope, int maxRetries)
    {
        if (endpoint.BasicAuth is { } basic)
            return RequestBuilder.BasicAuthorization(basic, scope);
        if (endpoint.OAuth2 is not { } oauth2)
            return null;

        _tokens ??= new OAuth2Tokens(_httpClient, _log, Time);
        return $"Bearer {await _tokens.GetAsync(oauth2, scope, maxRetries)}";
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
    private UntilPending? CheckUntil(RetryUntil until, HttpResponseMessage response, byte[] body, CallScope scope)
    {
        var checks = until.Checks;
        int status = (int)response.StatusCode;
        if (checks.Status.Count > 0 && !checks.Status.Contains(status))
            return new UntilPending($"status {status}, waiting for {string.Join(" or ", checks.Status)}", []);

        var results = EvaluateHeaders(checks.Headers, response, scope);
        results.AddRange(EvaluateCookies(checks.Cookies, response, scope));
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

    private IEnumerable<AssertionResult> EvaluateCookies(IReadOnlyList<Check> checks, HttpResponseMessage response, CallScope scope) =>
        checks.SelectMany(check => check.Conditions.Select(condition =>
            CheckEvaluator.EvaluateCookie(check.Subject, condition, CookieValue(response, check.Subject), scope)));

    /// <summary>
    /// The value of a cookie in the jar after the response, among those sent to the response's URL
    /// (the last one of a redirect); null when there is none. Names are case-sensitive, as in HTTP.
    /// </summary>
    private string? CookieValue(HttpResponseMessage response, string name) =>
        response.RequestMessage?.RequestUri is { } uri
            ? _cookies.GetCookies(uri).FirstOrDefault(c => c.Name == name)?.Value
            : null;

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
        else if (!expect.AnyStatus && status is < 200 or > 299)
        {
            Fail(report, $"Expected a 2xx status, got {status} (set expect.status to expect another one)");
            return;
        }

        report.Assertions.AddRange(EvaluateHeaders(expect.Headers, response, scope));
        report.Assertions.AddRange(EvaluateCookies(expect.Cookies, response, scope));

        JsonDocument? json = null;
        if (expect.Body.Count > 0 || step.Save.Values.Any(s => s.Path is not null))
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

            foreach (var (name, source) in step.Save)
            {
                JsonElement saved;
                if (source.Cookie is { } cookie)
                {
                    if (CookieValue(response, cookie) is not { } cookieValue)
                    {
                        Fail(report, $"Cannot save '{name}': no cookie '{cookie}' after this response");
                        return;
                    }
                    saved = JsonValues.FromString(cookieValue);
                }
                else if (JsonPath.TrySelect(json!.RootElement, source.Path!, out var value))
                {
                    saved = value.Clone();
                }
                else
                {
                    Fail(report, $"Cannot save '{name}': {source.Path} not found in the response");
                    return;
                }

                globals.Save(name, saved);
                report.Saved.Add(new(name, saved));
                _log.WriteLine($"Saved {name} = {JsonValues.ToDisplay(saved)}");
            }
        }
    }

    private static async Task<RecordedRequest> RecordAsync(HttpRequestMessage request, CookieContainer cookies) => new()
    {
        Method = request.Method.Method,
        Url = request.RequestUri?.ToString() ?? "",
        Headers = WithJarCookies(Headers(request.Headers, request.Content?.Headers), request.RequestUri, cookies),
        Body = request.Options.TryGetValue(RequestBuilder.RecordedBody, out var recorded) ? recorded
            : request.Content is null ? null
            : await request.Content.ReadAsStringAsync()
    };

    private static RecordedResponse Record(HttpResponseMessage response, byte[] body) => new()
    {
        Status = (int)response.StatusCode,
        Reason = response.ReasonPhrase,
        Headers = Headers(response.Headers, response.Content.Headers),
        Body = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, MaxRecordedBodyBytes)),
        BodyTruncated = body.Length > MaxRecordedBodyBytes
    };

    private static List<KeyValuePair<string, string>> Headers(HttpHeaders headers, HttpHeaders? contentHeaders) =>
        headers.Concat(contentHeaders ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .Select(h => new KeyValuePair<string, string>(h.Key, string.Join(", ", h.Value)))
            .ToList();

    /// <summary>
    /// The handler adds the jar's cookies after the request is recorded, so they are added here as it sends them:
    /// after a <c>Cookie</c> header written in the API file, in the same header.
    /// </summary>
    private static List<KeyValuePair<string, string>> WithJarCookies(
        List<KeyValuePair<string, string>> headers, Uri? uri, CookieContainer cookies)
    {
        if (uri is null || cookies.GetCookieHeader(uri) is not { Length: > 0 } fromJar)
            return headers;

        int index = headers.FindIndex(h => h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            headers.Add(new("Cookie", fromJar));
        else
            headers[index] = new(headers[index].Key, $"{headers[index].Value}; {fromJar}");
        return headers;
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
