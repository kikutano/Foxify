using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Foxify.Core.Models;

namespace Foxify.Core.Execution;

public class WorkflowEngine : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, object> _variables;
    private readonly HashSet<string> _executedFunctions;

    public WorkflowEngine(HttpClient httpClient)
    {
        _httpClient = httpClient;
        // Read base URL from environment variables (will be set during workflow execution)
        _variables = new Dictionary<string, object>();
        _executedFunctions = new HashSet<string>();
    }

    public async Task<WorkflowReport> ExecuteWorkflowAsync(WorkflowTemplate workflow)
    {
        var workflowReport = new WorkflowReport
        {
            WorkflowName = workflow.Metadata.Name
        };

        // Initialize variables with any initial values from the workflow template
        foreach (var variable in workflow.Variables)
        {
            _variables[variable.Key] = variable.Value;
        }

        // Set base URL from environment variables if available
        //TODO: Variables should be resolved from the workflow's environment variables, not from the system environment variables.
        ///PERO' giustamente lui non sa che quella variabile è una baseurl come fa a saperlo?
        if (workflow.EnvironmentVariables.TryGetValue("baseUrlDev", out var baseUrl))
        {
            _httpClient.BaseAddress = new Uri(baseUrl.ToString());
        }

        Console.WriteLine($"Executing workflow: {workflow.Metadata.Name}");

        foreach (var step in workflow.Workflow)
        {
            if (step.Type == "function")
            {
                var stepReport = await ExecuteFunctionStepAsync(workflow, step);
                workflowReport.StepReports.Add(stepReport);

                if (!stepReport.IsSuccess)
                {
                    Console.WriteLine($"Step '{step.Name}' failed. Stopping workflow execution.");
                    break; // Stop executing further steps if a step fails
                }
            }
            else if (step.Type == "DELAY")
            {
                await ExecuteDelayStepAsync(step);
            }
        }

        return workflowReport;
    }

    // Removed the GetBaseAddressFromEnvironment method since we'll use environment variables from YAML

    private async Task<StepReport> ExecuteFunctionStepAsync(WorkflowTemplate workflow, WorkflowStep step)
    {
        if (string.IsNullOrEmpty(step.FunctionName))
            return new StepReport { StepName = step.Name };

        if (!workflow.Functions.TryGetValue(step.FunctionName, out var function))
        {
            Console.WriteLine($"Function '{step.FunctionName}' not found");
            return new StepReport { StepName = step.Name };
        }

        HttpResponseMessage? response = null;

        // Check dependencies
        foreach (var dependency in step.DependsOn ?? [])
        {
            if (!_executedFunctions.Contains(dependency))
            {
                Console.WriteLine($"Dependency '{dependency}' not available");
                return new StepReport { StepName = step.Name };
            }
        }

        try
        {
            var httpMethod = ParseHttpMethod(function);

            var request = new HttpRequestMessage(
                httpMethod,
                ResolveVariables(function.Endpoint)
            );

            // Set body if present
            if (!string.IsNullOrEmpty(function.Body))
            {
                request.Content = new StringContent(ResolveVariables(function.Body));

                // Add content headers if they exist - but don't add Content-Type here as it's set by StringContent
                foreach (var header in function.Headers)
                {
                    if (!header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Headers.Add(header.Key, ResolveVariables(header.Value));
                    }
                    else
                    {
                        // If Content-Type is explicitly provided in headers, we should set it on the Content object itself
                        // This ensures that even when StringContent is used, the correct content type is preserved
                        request.Content = new StringContent(
                            ResolveVariables(function.Body),
                            System.Text.Encoding.UTF8,
                            header.Value
                        );
                    }
                }
            }
            else
            {
                // Add regular headers if no body
                foreach (var header in function.Headers)
                {
                    if (!header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Headers.Add(header.Key, ResolveVariables(header.Value));
                    }
                }
            }

            Console.WriteLine($"Executing {function.Method} {function.Endpoint}");

            response = await _httpClient.SendAsync(request);

            Console.WriteLine($"Response Status: {response.StatusCode}");

            // Extract variables if defined
            if (function.Extract.Any())
            {
                try
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Response Content: {responseContent}");

                    var jsonDocument = JsonDocument.Parse(responseContent);

                    foreach (var extract in function.Extract)
                    {
                        var value = ExtractValueFromJson(jsonDocument, extract.Value);

                        if (value != null)
                        {
                            _variables[extract.Key] = value;
                            Console.WriteLine($"Extracted {extract.Key}: {value}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error extracting variables: {ex.Message}");
                }
            }

            if (function.Excepted.Any())
            {
                foreach (var excepted in function.Excepted)
                {
                    if (excepted.Key.Equals("status_code", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Enum.TryParse<HttpStatusCode>(excepted.Value, out var expectedStatusCode))
                        {
                            if (response.StatusCode != expectedStatusCode)
                            {
                                Console.WriteLine($"Expected status code {expectedStatusCode}, but got {response.StatusCode}!");
                                return new StepReport
                                {
                                    StepName = step.Name,
                                    ExceptedStatusCode = response.StatusCode,
                                    IsSuccess = false
                                };
                            }
                        }
                    }
                }
            }

            // Mark this function as executed
            _executedFunctions.Add(step.FunctionName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing function {step.FunctionName}: {ex.Message}");
        }

        return new StepReport
        {
            StepName = step.Name,
            ExceptedStatusCode = response!.StatusCode,
            IsSuccess = true
        };
    }

    private static HttpMethod ParseHttpMethod(FunctionDefinition function)
    {
        // Parse HTTP method from function definition
        HttpMethod httpMethod = HttpMethod.Get;
        if (function.Method.ToLower().Equals("post", StringComparison.OrdinalIgnoreCase))
        {
            httpMethod = HttpMethod.Post;
        }
        else if (function.Method.ToLower().Equals("put", StringComparison.OrdinalIgnoreCase))
        {
            httpMethod = HttpMethod.Put;
        }
        else if (function.Method.ToLower().Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            httpMethod = HttpMethod.Delete;
        }

        return httpMethod;
    }

    private async Task ExecuteDelayStepAsync(WorkflowStep step)
    {
        if (step.DurationSeconds.HasValue)
        {
            Console.WriteLine($"Waiting for {step.DurationSeconds.Value} seconds");
            await Task.Delay(TimeSpan.FromSeconds(step.DurationSeconds.Value));
        }
    }

    private string ResolveVariables(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Simple variable replacement - in a real implementation this would be more sophisticated
        var pattern = @"\$\{([^}]+)\}";
        return Regex.Replace(input, pattern, match =>
        {
            var variableName = match.Groups[1].Value;
            if (_variables.TryGetValue(variableName, out var value))
            {
                return value.ToString() ?? string.Empty;
            }
            return match.Value; // Return original if not found
        });
    }

    private object ExtractValueFromJson(JsonDocument jsonDocument, string jsonPath)
    {
        // Simple JSONPath implementation for basic extraction
        // This handles simple paths like $.token or $.data.id

        if (string.IsNullOrEmpty(jsonPath))
            return null;

        if (!jsonPath.StartsWith("$."))
            return null;

        var pathParts = jsonPath.Substring(2).Split('.');
        var currentElement = jsonDocument.RootElement;

        try
        {
            foreach (var part in pathParts)
            {
                if (currentElement.ValueKind == JsonValueKind.Object)
                {
                    if (currentElement.TryGetProperty(part, out var property))
                    {
                        currentElement = property;
                    }
                    else
                    {
                        return null;
                    }
                }
                else if (currentElement.ValueKind == JsonValueKind.Array && int.TryParse(part, out var index))
                {
                    if (index >= 0 && index < currentElement.GetArrayLength())
                    {
                        currentElement = currentElement[index];
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }

            return currentElement.ValueKind == JsonValueKind.Null ? null : currentElement.ToString();
        }
        catch
        {
            return null;
        }
    }

    public void SetVariable(string name, object value)
    {
        _variables[name] = value;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
