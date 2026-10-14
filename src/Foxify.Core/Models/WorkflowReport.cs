using System.Net;

namespace Foxify.Core.Models;

public class StepReport
{
    public string StepName { get; set; } = string.Empty;
    public string? Method { get; set; }
    public Uri? Url { get; set; }
    /// <summary>Status code received from the server; null when no response was received.</summary>
    public HttpStatusCode? StatusCode { get; set; }
    /// <summary>The <c>expected.status_code</c> of the function, if any.</summary>
    public HttpStatusCode? ExpectedStatusCode { get; set; }
    public bool IsSuccess { get; set; } = true;
    /// <summary>Why the step failed when the reason is not a failed assertion (status, error, ...).</summary>
    public string? FailureReason { get; set; }
    public List<AssertionResult> Assertions { get; set; } = new();
    /// <summary>Duration of the HTTP request (last attempt when retried).</summary>
    public TimeSpan Duration { get; set; }
    public long ExecutionTimeMilliseconds => (long)Duration.TotalMilliseconds;
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
}

public class AssertionResult
{
    public string Variable { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string ActualValue { get; set; } = string.Empty;
    public bool Passed { get; set; }
}

public class WorkflowReport
{
    public string WorkflowName { get; set; } = string.Empty;
    public List<StepReport> StepReports { get; set; } = new List<StepReport>();
}
