using tyfapi.cli.Tyfapi;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

Console.WriteLine("Enter the path to the YAML file:");
string filePath = "C:\\Users\\Tano\\source\\repos\\kikutano\\tyfapi\\docs\\login_flow_example.yaml"; // Hardcoded for testing

if (string.IsNullOrWhiteSpace(filePath))
{
    Console.WriteLine("No file path provided.");
    return;
}

if (!File.Exists(filePath))
{
    Console.WriteLine("File not found.");
    return;
}

try
{
    var yamlContent = File.ReadAllText(filePath);
    Console.WriteLine($"Loading workflow from: {filePath}");

    // Load environment variables from environment.yaml file
    Dictionary<string, object> environmentVariables = new();
    string envFilePath = "C:\\Users\\Tano\\source\\repos\\kikutano\\tyfapi\\docs\\environment.yaml";
    if (File.Exists(envFilePath))
    {
        var envDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var envContent = File.ReadAllText(envFilePath);
        environmentVariables = envDeserializer.Deserialize<Dictionary<string, object>>(envContent) ?? new();
    }

    var parser = new TyfapiParser();
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

    // Create engine and execute workflow
    using var engine = new TyfapiEngine();
    await engine.ExecuteWorkflowAsync(workflow);

    Console.WriteLine("Workflow execution completed!");
}
catch (Exception ex)
{
    Console.WriteLine($"Error processing workflow: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}
