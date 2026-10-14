using Foxify.Core.Models;
using System.Diagnostics;
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
            if (IsStepType(step, "DELAY"))
            {
                await ExecuteDelayStepAsync(step);
                continue;
            }

            var stepReport = IsStepType(step, "function")
                ? await ExecuteFunctionStepAsync(workflow, step)
                : Fail(GetStepName(step), $"Unknown step type '{step.Type}'");

            report.StepReports.Add(stepReport);

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

    private void SetVariables(Dictionary<string, object> variables)
    {
        foreach (var (name, value) in variables)
            _variables[name] = value;
    }

    private async Task<StepReport> ExecuteFunctionStepAsync(WorkflowTemplate workflow, WorkflowStep step)
    {
        var stepName = GetStepName(step);

        if (string.IsNullOrEmpty(step.FunctionName))
            return Fail(stepName, $"Step '{stepName}' has no function_name");

        if (!workflow.Functions.TryGetValue(step.FunctionName, out var function))
            return Fail(stepName, $"Function '{step.FunctionName}' not found");

        var missingDependency = step.DependsOn?.FirstOrDefault(d => !_executedFunctions.Contains(d));
        if (missingDependency is not null)
            return Fail(stepName, $"Dependency '{missingDependency}' not available");

        var stopwatch = new Stopwatch();
        try
        {
            var maxRetries = Math.Max(0, workflow.Settings?.MaxRetries ?? 0);
            using var response = await SendWithRetriesAsync(function, maxRetries, stopwatch);

            _log.WriteLine($"Response Status: {response.StatusCode}");
            _log.WriteLine($"Execution Time: {stopwatch.ElapsedMilliseconds} ms");

            var failure = await CheckResponseAsync(function, response);
            if (failure is not null)
                return Fail(stepName, failure, response.StatusCode, stopwatch.ElapsedMilliseconds);

            _executedFunctions.Add(step.FunctionName);

            return new StepReport
            {
                StepName = stepName,
                StatusCode = response.StatusCode,
                IsSuccess = true,
                ExecutionTimeMilliseconds = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            return Fail(stepName, $"Error executing function {step.FunctionName}: {ex.Message}",
                elapsedMilliseconds: stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Sends the request, retrying up to <paramref name="maxRetries"/> times on transient
    /// failures (network errors, timeouts). HTTP error status codes are never retried:
    /// they are a valid response, checked against <c>expected</c>.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetriesAsync(
        FunctionDefinition function, int maxRetries, Stopwatch stopwatch)
    {
        for (int attempt = 0; ; attempt++)
        {
            // A request message can be sent only once, so it is rebuilt on every attempt.
            using var request = BuildRequest(function);
            if (attempt == 0)
                _log.WriteLine($"Executing {request.Method} {request.RequestUri}");

            try
            {
                stopwatch.Restart();
                var response = await _httpClient.SendAsync(request);
                stopwatch.Stop();
                return response;
            }
            catch (Exception ex) when (attempt < maxRetries && ex is HttpRequestException or TaskCanceledException)
            {
                stopwatch.Stop();
                var delay = RetryBaseDelay * (attempt + 1);
                _log.WriteLine($"{ex.Message} Retrying in {delay.TotalMilliseconds} ms ({attempt + 1}/{maxRetries})");
                await Task.Delay(delay);
            }
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
            baseUrl = fallback?.ToString() ?? string.Empty;

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
    /// Checks the expected status code, extracts variables and evaluates the asserts.
    /// Returns null when the response is valid, otherwise the reason of the failure.
    /// </summary>
    private async Task<string?> CheckResponseAsync(FunctionDefinition function, HttpResponseMessage response)
    {
        var expectedStatus = function.Expected
            .FirstOrDefault(e => e.Key.Equals("status_code", StringComparison.OrdinalIgnoreCase)).Value;

        if (expectedStatus is not null)
        {
            if (!Enum.TryParse<HttpStatusCode>(expectedStatus, out var expectedStatusCode))
                return $"Invalid expected status_code '{expectedStatus}'";

            if (response.StatusCode != expectedStatusCode)
                return $"Expected status code {expectedStatusCode}, but got {response.StatusCode}!";
        }

        var extracted = new Dictionary<string, string?>();
        if (function.Extract.Count > 0)
        {
            var body = await response.Content.ReadAsStringAsync();
            _log.WriteLine($"Response Content: {body}");

            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                return $"Response is not valid JSON, cannot extract variables: {ex.Message}";
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

        foreach (var (name, expression) in function.Asserts)
        {
            var actualValue = extracted.GetValueOrDefault(name) ?? "null";
            if (!AssertEvaluator.Evaluate(actualValue, expression))
                return $"Assert failed for '{name}': actual '{actualValue}', expected '{expression}'";
        }

        return null;
    }

    private async Task ExecuteDelayStepAsync(WorkflowStep step)
    {
        if (step.DurationSeconds is not > 0)
            return;

        _log.WriteLine($"Waiting for {step.DurationSeconds} seconds");
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
                ? value?.ToString() ?? string.Empty
                : throw new InvalidOperationException($"Variable '{name}' is not defined.");
        });
    }

    private StepReport Fail(string stepName, string reason,
        HttpStatusCode? statusCode = null, long elapsedMilliseconds = 0)
    {
        _log.WriteLine(reason);

        return new StepReport
        {
            StepName = stepName,
            StatusCode = statusCode,
            IsSuccess = false,
            ExecutionTimeMilliseconds = elapsedMilliseconds
        };
    }

    private static bool IsStepType(WorkflowStep step, string type) =>
        string.Equals(step.Type, type, StringComparison.OrdinalIgnoreCase);

    private static string GetStepName(WorkflowStep step) =>
        !string.IsNullOrEmpty(step.Name) ? step.Name : step.FunctionName ?? step.Type;

    // Any verb works (GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS...); defaults to GET.
    private static HttpMethod ParseHttpMethod(string method) =>
        string.IsNullOrWhiteSpace(method) ? HttpMethod.Get : new HttpMethod(method.Trim().ToUpperInvariant());
}
