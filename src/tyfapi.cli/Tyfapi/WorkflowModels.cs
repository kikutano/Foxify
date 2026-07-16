using System.Collections.Generic;

namespace tyfapi.cli.Tyfapi;

public class WorkflowMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = string.Empty;
    public Dictionary<string, object> Variables { get; set; } = new();
}

public class FunctionDefinition
{
    public string Type { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Baseurl { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, string> Extract { get; set; } = new();
}

public class WorkflowStep
{
    public string? Name { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? FunctionName { get; set; }
    public List<string>? DependsOn { get; set; }
    public double? DurationSeconds { get; set; }
}

public class WorkflowSettings
{
    public int Timeout { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
}

public sealed class WorkflowTemplate
{
    public WorkflowMetadata Metadata { get; set; } = new();
    public Dictionary<string, FunctionDefinition> Functions { get; set; } = new();
    public List<WorkflowStep> Workflow { get; set; } = new();
    public WorkflowSettings Settings { get; set; } = new();
    public Dictionary<string, object> Variables { get; set; } = new();
    public Dictionary<string, object> EnvironmentVariables { get; set; } = new();
}
