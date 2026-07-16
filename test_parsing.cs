using System;
using System.IO;
using tyfapi.cli.Tyfapi;

class TestParsing
{
    static void Main(string[] args)
    {
        Console.WriteLine("Testing WorkflowTemplate Variables Parsing");
        
        try
        {
            var parser = new TyfapiParser();
            
            // Test with the variables file
            string yamlContent = File.ReadAllText("docs/test_variables.yaml");
            var workflow = parser.Parse(yamlContent);
            
            Console.WriteLine($"Workflow Name: {workflow.Metadata.Name}");
            Console.WriteLine($"Variables Count: {workflow.Variables.Count}");
            
            foreach (var variable in workflow.Variables)
            {
                Console.WriteLine($"Variable '{variable.Key}': {variable.Value}");
            }
            
            Console.WriteLine("Parsing test completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}