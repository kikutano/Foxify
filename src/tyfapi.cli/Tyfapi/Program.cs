using System.Text.Json;
using tyfapi.cli.Tyfapi;

namespace tyfapi.cli.Tyfapi;

class Program
{
    static async Task Main(string[] args)
    {
        // Direct approach to test variables - bypassing TyfapiConsole.Run
        string filePath = "docs/test_variables.yaml";
        
        if (args != null && args.Length > 0)
        {
            filePath = args[0];
        }
        
        Console.WriteLine($"Loading workflow from: {filePath}");
        
        try
        {
            // Test the parser with a simple workflow that includes variables
            var parser = new TyfapiParser();
            var yamlContent = File.ReadAllText(filePath);
            var workflow = parser.Parse(yamlContent);
            
            Console.WriteLine($"Workflow: {workflow.Metadata.Name}");
            Console.WriteLine($"Variables count: {workflow.Variables.Count}");
            
            foreach (var variable in workflow.Variables)
            {
                Console.WriteLine($"Variable '{variable.Key}': {variable.Value}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing workflow: {ex.Message}");
        }
    }
}
