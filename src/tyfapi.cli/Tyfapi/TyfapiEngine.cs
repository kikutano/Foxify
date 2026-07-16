using System.Text.Json;
using System.Text.RegularExpressions;

namespace tyfapi.cli.Tyfapi;

public class TyfapiEngine : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, object> _variables;

    public TyfapiEngine()
    {
        _httpClient = new HttpClient();
        // Read base URL from environment variables (will be set during workflow execution)
        var baseAddress = "http://localhost";
        _httpClient.BaseAddress = new Uri(baseAddress);
        _variables = new Dictionary<string, object>();
    }

    public async Task ExecuteWorkflowAsync(WorkflowTemplate workflow)
    {
        // Initialize variables with any initial values from the workflow template
        foreach (var variable in workflow.Variables)
        {
            _variables[variable.Key] = variable.Value;
        }

        // Set base URL from environment variables if available
        //TODO: Variables should be resolved from the workflow's environment variables, not from the system environment variables.
        if (workflow.EnvironmentVariables.TryGetValue("baseUrlDev", out var baseUrl))
        {
            _httpClient.BaseAddress = new Uri(baseUrl.ToString());
        }

        Console.WriteLine($"Executing workflow: {workflow.Metadata.Name}");

        foreach (var step in workflow.Workflow)
        {
            if (step.Type == "function")
            {
                await ExecuteFunctionStepAsync(workflow, step);
            }
            else if (step.Type == "DELAY")
            {
                await ExecuteDelayStepAsync(step);
            }
        }
    }

    // Removed the GetBaseAddressFromEnvironment method since we'll use environment variables from YAML

    private async Task ExecuteFunctionStepAsync(WorkflowTemplate workflow, WorkflowStep step)
    {
        if (string.IsNullOrEmpty(step.FunctionName))
            return;

        if (!workflow.Functions.TryGetValue(step.FunctionName, out var function))
        {
            Console.WriteLine($"Function '{step.FunctionName}' not found");
            return;
        }

        // Check dependencies
        foreach (var dependency in step.DependsOn ?? [])
        {
            if (!_variables.ContainsKey(dependency))
            {
                Console.WriteLine($"Dependency '{dependency}' not available");
                return;
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

            var response = await _httpClient.SendAsync(request);

            Console.WriteLine($"Response Status: {response.StatusCode}");

            // Extract variables if defined
            foreach (var extract in function.Extract)
            {
                // Parse JSON response and extract values based on JSONPath expression
                try
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Response Content: {responseContent}");

                    var jsonDocument = JsonDocument.Parse(responseContent);
                    var value = ExtractValueFromJson(jsonDocument, extract.Value);

                    if (value != null)
                    {
                        _variables[extract.Key] = value;
                        Console.WriteLine($"Extracted {extract.Key}: {value}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error extracting {extract.Key}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing function {step.FunctionName}: {ex.Message}");
        }
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
