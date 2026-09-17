using System.Net;

namespace Foxify.Core.Models;

public class StepReport
{
    public string StepName { get; set; } = string.Empty;
    public HttpStatusCode ExceptedStatusCode { get; set; } = HttpStatusCode.OK;
    public bool IsSuccess { get; set; } = true;
}

public class WorkflowReport
{
    public string WorkflowName { get; set; } = string.Empty;
    public List<StepReport> StepReports { get; set; } = new List<StepReport>();
}