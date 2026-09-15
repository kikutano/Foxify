namespace Foxify.Core.Models;

public class WorkflowSettings
{
    public int Timeout { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
}
