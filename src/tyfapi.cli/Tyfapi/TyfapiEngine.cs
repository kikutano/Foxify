using System.Text.RegularExpressions;

namespace tyfapi.cli.Tyfapi;

public class TyfapiEngine
{
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, object> _variables;

    public TyfapiEngine()
    {
        _httpClient = new HttpClient();
        _variables = new Dictionary<string, object>();
    }

    public async Task ExecuteWorkflowAsync(WorkflowTemplate workflow)
    {
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
            // Parse HTTP method from function definition
            var httpMethod = HttpMethod.Get;
            //if (Enum.TryParse<HttpMethod>(function.Method, true, out var parsedMethod))
            //{
            //    httpMethod = parsedMethod;
            //}

            var request = new HttpRequestMessage(
                httpMethod,
                ResolveVariables(function.Endpoint)
            );

            // Set body if present
            if (!string.IsNullOrEmpty(function.Body))
            {
                request.Content = new StringContent(ResolveVariables(function.Body));

                // Add content headers if they exist
                foreach (var header in function.Headers)
                {
                    if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Content.Headers.Add(header.Key, ResolveVariables(header.Value));
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
                // In a real implementation, we would parse the JSON response
                // and extract values based on the JSONPath expression
                Console.WriteLine($"Extracting {extract.Key} from response");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing function {step.FunctionName}: {ex.Message}");
        }
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

    public void SetVariable(string name, object value)
    {
        _variables[name] = value;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}