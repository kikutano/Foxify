namespace Tyfapi.Core.Models;

public sealed class WorkflowTemplate
{
    public WorkflowMetadata Metadata { get; set; } = new();
    public Dictionary<string, FunctionDefinition> Functions { get; set; } = new();
    public List<WorkflowStep> Workflow { get; set; } = new();
    public WorkflowSettings Settings { get; set; } = new();
    public Dictionary<string, object> Variables { get; set; } = new();
    public Dictionary<string, object> EnvironmentVariables { get; set; } = new();
}
