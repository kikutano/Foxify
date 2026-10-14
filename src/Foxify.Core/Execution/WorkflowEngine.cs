using Foxify.Core.Models;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Foxify.Core.Execution;

/// <summary>
/// Runs a parsed workflow: executes the steps in order, carries extracted variables
/// from one HTTP call to the next and stops at the first failing step.
/// The <see cref="HttpClient"/> is owned by the caller and is not disposed by the engine.
/// </summary>
public partial class WorkflowEngine
{
    private const string BaseUrlVariable = "baseUrl";
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(250);

    private readonly HttpClient _httpClient;
    private readonly TextWriter _log;
    private readonly Dictionary<string, object> _variables = new();
    private readonly HashSet<string> _executedFunctions = new();

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex VariablePlaceholder();

    /// <summary>Raised before each step runs, with a short description of the step.</summary>
    public event Action<string>? StepStarted;

    /// <summary>Raised after each function step, successful or not.</summary>
    public event Action<StepReport>? StepCompleted;

    public WorkflowEngine(HttpClient httpClient, TextWriter? log = null)
    {
        _httpClient = httpClient;
        _log = log ?? Console.Out;
    }

    public async Task<WorkflowReport> ExecuteWorkflowAsync(WorkflowTemplate workflow)
    {
        var report = new WorkflowReport { WorkflowName = workflow.Metadata.Name };

        // Variables declared in the workflow are defaults; the environment file overrides them,
        // so the same workflow can run against different environments.
        SetVariables(workflow.Metadata.Variables);
        SetVariables(workflow.Variables);
        SetVariables(workflow.EnvironmentVariables);

        _log.WriteLine($"Executing workflow: {workflow.Metadata.Name}");

        foreach (var step in workflow.Workflow)
        {
            if (IsDelayStep(step))
            {
                await ExecuteDelayStepAsync(step);
                continue;
            }

            var stepReport = new StepReport { StepName = GetStepName(step) };
            StepStarted?.Invoke(stepReport.StepName);

            if (IsStepType(step, "function"))
                await ExecuteFunctionStepAsync(workflow, step, stepReport);
            else
                Fail(stepReport, $"Unknown step type '{step.Type}'");

            report.StepReports.Add(stepReport);
            StepCompleted?.Invoke(stepReport);

            if (!stepReport.IsSuccess)
            {
                _log.WriteLine($"Step '{stepReport.StepName}' failed. Stopping workflow execution.");
                break;
            }

            _log.WriteLine($"Step '{stepReport.StepName}' executed successfully.");
        }

        return report;
    }

    public void SetVariable(string name, object value)
    {
        _variables[name] = value;
    }

    /// <summary>The name shown for a step: its <c>name</c>, else its function name, else its type.</summary>
    public static string GetStepName(WorkflowStep step) =>
        !string.IsNullOrEmpty(step.Name) ? step.Name : step.FunctionName ?? step.Type;

    public static bool IsDelayStep(WorkflowStep step) => IsStepType(step, "DELAY");

    private void SetVariables(Dictionary<string, object> variables)
    {
        foreach (var (name, value) in variables)
            _variables[name] = value;
    }

    private async Task ExecuteFunctionStepAsync(WorkflowTemplate workflow, WorkflowStep step, StepReport report)
    {
        if (string.IsNullOrEmpty(step.FunctionName))
        {
            Fail(report, $"Step '{report.StepName}' has no function_name");
            return;
        }

        if (!workflow.Functions.TryGetValue(step.FunctionName, out var function))
        {
            Fail(report, $"Function '{step.FunctionName}' not found");
            return;
        }

        var missingDependency = step.DependsOn?.FirstOrDefault(d => !_executedFunctions.Contains(d));
        if (missingDependency is not null)
        {
            Fail(report, $"Dependency '{missingDependency}' not available");
            return;
        }

        try
        {
            var maxRetries = Math.Max(0, workflow.Settings?.MaxRetries ?? 0);
            using var response = await SendWithRetriesAsync(function, maxRetries, report);

            _log.WriteLine($"Response Status: {response.StatusCode}");
            _log.WriteLine($"Execution Time: {report.ExecutionTimeMilliseconds} ms");

            await CheckResponseAsync(function, response, report);

            if (report.IsSuccess)
                _executedFunctions.Add(step.FunctionName);
        }
        catch (Exception ex)
        {
            Fail(report, $"Error executing function {step.FunctionName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends the request, retrying up to <paramref name="maxRetries"/> times on transient
    /// failures (network errors, timeouts). HTTP error status codes are never retried:
    /// they are a valid response, checked against <c>expected</c>.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetriesAsync(
        FunctionDefinition function, int maxRetries, StepReport report)
    {
        for (int attempt = 0; ; attempt++)
        {
            // A request message can be sent only once, so it is rebuilt on every attempt.
            using var request = BuildRequest(function);
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

    private HttpRequestMessage BuildRequest(FunctionDefinition function)
    {
        var request = new HttpRequestMessage(ParseHttpMethod(function.Method), BuildRequestUri(function));

        if (!string.IsNullOrEmpty(function.Body))
            request.Content = new StringContent(ResolveVariables(function.Body), Encoding.UTF8);

        foreach (var (name, value) in function.Headers)
        {
            var resolvedValue = ResolveVariables(value);
            if (request.Headers.TryAddWithoutValidation(name, resolvedValue))
                continue;

            // Content headers (Content-Type, Content-Language...) belong to the body
            // and are ignored when the request has none.
            if (request.Content is not null)
            {
                request.Content.Headers.Remove(name);
                request.Content.Headers.TryAddWithoutValidation(name, resolvedValue);
            }
        }

        return request;
    }

    private Uri BuildRequestUri(FunctionDefinition function)
    {
        var endpoint = ResolveVariables(function.Endpoint);

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var absoluteUri)
            && (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
        {
            return absoluteUri;
        }

        // The function's own baseurl wins; otherwise fall back to the baseUrl variable.
        var baseUrl = ResolveVariables(function.Baseurl);
        if (string.IsNullOrWhiteSpace(baseUrl) && _variables.TryGetValue(BaseUrlVariable, out var fallback))
            baseUrl = ToInvariantString(fallback);

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            // Relative to HttpClient.BaseAddress, if any.
            return new Uri(endpoint, UriKind.RelativeOrAbsolute);
        }

        // Plain concatenation so a base path (e.g. https://host/v1/) is preserved
        // regardless of leading/trailing slashes.
        return new Uri($"{baseUrl.TrimEnd('/')}/{endpoint.TrimStart('/')}");
    }

    /// <summary>
    /// Checks the expected status code, extracts variables and evaluates every assert,
    /// recording the outcome in <paramref name="report"/>.
    /// </summary>
    private async Task CheckResponseAsync(FunctionDefinition function, HttpResponseMessage response, StepReport report)
    {
        report.StatusCode = response.StatusCode;

        // JSON is UTF-8 by definition (RFC 8259), so the body is decoded as UTF-8.
        var bodyBytes = await response.Content.ReadAsByteArrayAsync();
        report.BytesReceived = bodyBytes.Length;
        var body = Encoding.UTF8.GetString(bodyBytes);
        _log.WriteLine($"Response Content: {body}");

        var expectedStatus = function.Expected
            .FirstOrDefault(e => e.Key.Equals("status_code", StringComparison.OrdinalIgnoreCase)).Value;

        if (expectedStatus is not null)
        {
            if (!Enum.TryParse<HttpStatusCode>(expectedStatus, out var expectedStatusCode))
            {
                Fail(report, $"Invalid expected status_code '{expectedStatus}'");
                return;
            }

            report.ExpectedStatusCode = expectedStatusCode;
            if (response.StatusCode != expectedStatusCode)
            {
                Fail(report, $"Expected status code {(int)expectedStatusCode}, but got {(int)response.StatusCode}");
                return;
            }
        }

        var extracted = new Dictionary<string, string?>();
        if (function.Extract.Count > 0)
        {
            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                Fail(report, $"Response is not valid JSON, cannot extract variables: {ex.Message}");
                return;
            }

            using (json)
            {
                foreach (var (name, path) in function.Extract)
                {
                    var value = JsonPath.Select(json.RootElement, path);
                    extracted[name] = value;

                    if (value is not null)
                    {
                        _variables[name] = value;
                        _log.WriteLine($"Extracted {name}: {value}");
                    }
                }
            }
        }

        // Every assert is evaluated, so a report shows all of them and not only the first failure.
        foreach (var (name, expression) in function.Asserts)
        {
            var actualValue = extracted.GetValueOrDefault(name) ?? "null";
            var passed = AssertEvaluator.Evaluate(actualValue, expression);
            report.Assertions.Add(new AssertionResult
            {
                Variable = name,
                Expression = expression,
                ActualValue = actualValue,
                Passed = passed
            });

            if (!passed)
            {
                report.IsSuccess = false;
                _log.WriteLine($"Assert failed for '{name}': actual '{actualValue}', expected '{expression}'");
            }
        }
    }

    private async Task ExecuteDelayStepAsync(WorkflowStep step)
    {
        if (step.DurationSeconds is not > 0)
            return;

        StepStarted?.Invoke(FormattableString.Invariant($"delay {step.DurationSeconds}s"));
        _log.WriteLine(FormattableString.Invariant($"Waiting for {step.DurationSeconds} seconds"));
        await Task.Delay(TimeSpan.FromSeconds(step.DurationSeconds.Value));
    }

    /// <summary>Replaces every <c>${name}</c> with its value; unknown variables are an error.</summary>
    private string ResolveVariables(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return VariablePlaceholder().Replace(input, match =>
        {
            var name = match.Groups[1].Value;
            return _variables.TryGetValue(name, out var value)
                ? ToInvariantString(value)
                : throw new InvalidOperationException($"Variable '{name}' is not defined.");
        });
    }

    private void Fail(StepReport report, string reason)
    {
        _log.WriteLine(reason);
        report.IsSuccess = false;
        report.FailureReason = reason;
    }

    // Variables end up in URLs and bodies: numbers must not depend on the machine's culture (9.9, not 9,9).
    private static string ToInvariantString(object? value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

    private static bool IsStepType(WorkflowStep step, string type) =>
        string.Equals(step.Type, type, StringComparison.OrdinalIgnoreCase);

    // Any verb works (GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS...); defaults to GET.
    private static HttpMethod ParseHttpMethod(string method) =>
        string.IsNullOrWhiteSpace(method) ? HttpMethod.Get : new HttpMethod(method.Trim().ToUpperInvariant());
}
