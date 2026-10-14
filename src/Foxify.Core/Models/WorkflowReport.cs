using System.Net;

namespace Foxify.Core.Models;

public class StepReport
{
    public string StepName { get; set; } = string.Empty;
    /// <summary>Status code received from the server; null when no response was received.</summary>
    public HttpStatusCode? StatusCode { get; set; }
    public bool IsSuccess { get; set; } = true;
    public long ExecutionTimeMilliseconds { get; set; } = 0;
}

public class WorkflowReport
{
    public string WorkflowName { get; set; } = string.Empty;
    public List<StepReport> StepReports { get; set; } = new List<StepReport>();
}
