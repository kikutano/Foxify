namespace Tyfapi.Core.Models;

public class WorkflowStep
{
    public string? Name { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? FunctionName { get; set; }
    public List<string>? DependsOn { get; set; }
    public double? DurationSeconds { get; set; }
}
