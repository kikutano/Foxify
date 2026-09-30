using Foxify.Core.Models;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Foxify.Core.Execution;

public class WorkflowEngine : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, object> _variables;
    private readonly HashSet<string> _executedFunctions;
    private readonly Regex _arrayRegex = new Regex(@"^([^\[\]]+)\[(\d+)\]$", RegexOptions.Compiled);

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

        foreach (var envVar in workflow.EnvironmentVariables)
        {
            _variables[envVar.Key] = envVar.Value;
        }

        // Initialize variables with any initial values from the workflow template
        foreach (var variable in workflow.Variables)
        {
            _variables[variable.Key] = variable.Value;
        }

        // Set base URL from environment variables if available
        //TODO: Variables should be resolved from the workflow's environment variables, not from the system environment variables.
        ///PERO' giustamente lui non sa che quella variabile è una baseurl come fa a saperlo?
        //if (workflow.EnvironmentVariables.TryGetValue("baseUrl", out var baseUrl))
        //{
        //    _httpClient.BaseAddress = new Uri(baseUrl.ToString());
        //}

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
                else
                {
                    Console.WriteLine($"Step '{step.Name}' executed successfully.");
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
        var stopwatch = new Stopwatch();

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
            workflow.EnvironmentVariables.TryGetValue("baseUrl", out var fullBaseUrl);
            string fullEndpoint = ResolveVariables(function.Endpoint);

            var httpMethod = ParseHttpMethod(function);

            Uri requestUri;
            if (!string.IsNullOrEmpty(fullBaseUrl.ToString()))
            {
                var baseUri = new Uri(fullBaseUrl.ToString());
                requestUri = new Uri(baseUri, fullEndpoint);
            }
            else
            {
                requestUri = new Uri(fullEndpoint, UriKind.RelativeOrAbsolute);
            }

            var request = new HttpRequestMessage(httpMethod, requestUri);

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
                if (function.Headers is not null)
                {
                    foreach (var header in function.Headers)
                    {
                        string resolvedHeaderValue = ResolveVariables(header.Value);

                        if (!header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            request.Headers.TryAddWithoutValidation(header.Key, resolvedHeaderValue);
                        }
                    }
                }
            }

            Console.WriteLine($"Executing {function.Method} {function.Endpoint}");

            stopwatch.Start();
            response = await _httpClient.SendAsync(request);
            stopwatch.Stop();

            Console.WriteLine($"Response Status: {response.StatusCode}");
            Console.WriteLine($"Execution Time: {stopwatch.ElapsedMilliseconds} ms");

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

            if (function.Expected.Any())
            {
                foreach (var excepted in function.Expected)
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

            foreach (var (variableName, expression) in function.Asserts)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(responseContent);

                function.Extract.TryGetValue(variableName, out var keyName);
                var realValue = ExtractValueFromJson(jsonDocument, keyName);

                bool isSuccess = AssertEvaluator.Evaluate(realValue.ToString(), expression);

                if (!isSuccess)
                {
                    Console.WriteLine(
                        $"Assert fallito per la variabile '{variableName}': valore attuale '{keyName}', atteso '{expression}'");
                    return new StepReport
                    {
                        StepName = step.Name,
                        ExceptedStatusCode = response.StatusCode,
                        IsSuccess = false
                    };
                }
            }

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
            IsSuccess = true,
            ExecutionTimeMilliseconds = stopwatch.ElapsedMilliseconds
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

    private object? ExtractValueFromJson(JsonDocument jsonDocument, string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath) || !jsonPath.StartsWith("$."))
            return null;

        var path = jsonPath.Substring(2);
        var pathParts = path.Split('.');
        var currentElement = jsonDocument.RootElement;

        try
        {
            foreach (var part in pathParts)
            {
                var match = _arrayRegex.Match(part);

                if (match.Success)
                {
                    // È un accesso con array, es. "data[0]"
                    string propertyName = match.Groups[1].Value;
                    int arrayIndex = int.Parse(match.Groups[2].Value);

                    // 1. Accediamo alla proprietà dell'oggetto (es: "data")
                    if (currentElement.ValueKind == JsonValueKind.Object &&
                        currentElement.TryGetProperty(propertyName, out var arrayProperty))
                    {
                        currentElement = arrayProperty;
                    }
                    else
                    {
                        return null;
                    }

                    // 2. Accediamo all'elemento dell'array all'indice specificato (es: [0])
                    if (currentElement.ValueKind == JsonValueKind.Array &&
                        arrayIndex >= 0 && arrayIndex < currentElement.GetArrayLength())
                    {
                        currentElement = currentElement[arrayIndex];
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    // È un accesso a proprietà standard (es: "page", "email", "username")
                    if (currentElement.ValueKind == JsonValueKind.Object &&
                        currentElement.TryGetProperty(part, out var property))
                    {
                        currentElement = property;
                    }
                    else
                    {
                        return null;
                    }
                }
            }

            return currentElement.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.Number => currentElement.GetRawText(), // Mantiene la rappresentazione numerica precisa
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => currentElement.ToString()
            };
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
