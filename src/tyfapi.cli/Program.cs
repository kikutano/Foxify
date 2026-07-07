using tyfapi.cli.Tyfapi;

Console.WriteLine("Enter the path to the YAML file:");
//string filePath = Console.ReadLine();

string filePath = "C:\\Users\\Tano\\source\\repos\\kikutano\\tyfapi\\docs\\multiple_calls_with_delay.yaml"; // Hardcoded for testing

if (!string.IsNullOrWhiteSpace(filePath))
{
    // Load and parse the workflow
    if (File.Exists(filePath))
    {
        try
        {
            var yamlContent = File.ReadAllText(filePath);

            // In a real implementation, we would use a proper YAML parser like YamlDotNet
            // For now, we'll create a basic workflow execution using our TyfapiEngine

            Console.WriteLine("Workflow loaded successfully");
            Console.WriteLine($"Executing workflow from: {filePath}");

            // Create and execute the engine (simplified for demonstration)
            var engine = new TyfapiEngine();
            Console.WriteLine("Tyfapi Engine initialized");

            var parser = new TyfapiParser();
            // This would parse the YAML content into a WorkflowTemplate

            await engine.ExecuteWorkflowAsync(parser.Parse(yamlContent));

            // In a full implementation, we would parse the YAML into a WorkflowTemplate
            // and then pass it to the engine for execution
            Console.WriteLine("Workflow execution completed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing workflow: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine("File not found.");
    }
}
else
{
    Console.WriteLine("No file path provided.");
}