using System;
using System.IO;
using tyfapi.cli.Tyfapi;

class TestVariables
{
    static void Main(string[] args)
    {
        Console.WriteLine("Testing Variables Support");
        
        // Test with test_variables.yaml
        string filePath = "docs/test_variables.yaml";
        Console.WriteLine($"Loading workflow from: {filePath}");
        
        try
        {
            var parser = new TyfapiParser();
            var yamlContent = File.ReadAllText(filePath);
            var workflow = parser.Parse(yamlContent);
            
            Console.WriteLine($"Workflow: {workflow.Metadata.Name}");
            Console.WriteLine($"Variables count: {workflow.Variables.Count}");
            
            foreach (var variable in workflow.Variables)
            {
                Console.WriteLine($"Variable '{variable.Key}': {variable.Value}");
            }
            
            Console.WriteLine("Test completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}