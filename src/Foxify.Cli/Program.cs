using Foxify.Core.Execution;
using Foxify.Core.Parsing;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

//if (args.Length == 0)
//{
//    Console.WriteLine("Usage: Foxify <path-to-flow.yaml> [path-to-environment.yaml]");
//    return;
//}

//string filePath = args[0];

string filePath = "C:\\Users\\Tano\\source\\repos\\kikutano\\tyfapi\\docs\\complete_workflow.yaml";
string envFilePath = "C:\\Users\\Tano\\source\\repos\\kikutano\\tyfapi\\docs\\environment.yaml";

if (!File.Exists(filePath))
{
    Console.WriteLine($"File not found: {filePath}");
    return;
}

try
{
    var yamlContent = File.ReadAllText(filePath);
    Console.WriteLine($"Loading workflow from: {filePath}");

    // Load environment variables from an optional environment YAML file passed as the second argument.
    Dictionary<string, object> environmentVariables = new();
    //if (args.Length > 1)
    //{
    //string envFilePath = args[1];
    if (File.Exists(envFilePath))
    {
        var envDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var envContent = File.ReadAllText(envFilePath);
        environmentVariables = envDeserializer.Deserialize<Dictionary<string, object>>(envContent) ?? new();
    }
    else
    {
        Console.WriteLine($"Environment file not found: {envFilePath}");
    }
    //}

    var parser = new WorkflowParser();
    var workflow = parser.Parse(yamlContent, environmentVariables);

    Console.WriteLine($"Workflow: {workflow.Metadata.Name}");
    Console.WriteLine($"Metadata Variables count: {workflow.Metadata.Variables.Count}");
    Console.WriteLine($"Top-level Variables count: {workflow.Variables.Count}");
    Console.WriteLine($"Environment Variables count: {workflow.EnvironmentVariables.Count}");

    Console.WriteLine("Metadata Variables:");
    foreach (var variable in workflow.Metadata.Variables)
    {
        Console.WriteLine($"  '{variable.Key}': {variable.Value} ({variable.Value?.GetType().Name ?? "null"})");
    }

    Console.WriteLine("Top-level Variables:");
    foreach (var variable in workflow.Variables)
    {
        Console.WriteLine($"  '{variable.Key}': {variable.Value} ({variable.Value?.GetType().Name ?? "null"})");
    }

    Console.WriteLine("Environment Variables:");
    foreach (var variable in workflow.EnvironmentVariables)
    {
        Console.WriteLine($"  '{variable.Key}': {variable.Value} ({variable.Value?.GetType().Name ?? "null"})");
    }

    Console.WriteLine("\nExecuting workflow...");

    HttpClient httpClient = new HttpClient();
    using var engine = new WorkflowEngine(httpClient);
    await engine.ExecuteWorkflowAsync(workflow);

    Console.WriteLine("Workflow execution completed!");
}
catch (Exception ex)
{
    Console.WriteLine($"Error processing workflow: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}
