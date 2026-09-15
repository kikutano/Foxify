namespace Foxify.Core.Models;

public class WorkflowMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = string.Empty;
    public Dictionary<string, object> Variables { get; set; } = new();
}
