using System.Net;

namespace Foxify.Core.Models;

public class StepReport
{
    public string StepName { get; set; } = string.Empty;
    public string? Method { get; set; }
    public Uri? Url { get; set; }
    /// <summary>Status code received from the server; null when no response was received.</summary>
    public HttpStatusCode? StatusCode { get; set; }
    /// <summary>The status codes accepted by <c>expect.status</c>; empty when the step accepts any 2xx.</summary>
    public List<HttpStatusCode> ExpectedStatusCodes { get; set; } = new();
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

/// <summary>The outcome of one condition of <c>expect</c>, e.g. <c>$.total &gt;= 3</c>.</summary>
public class AssertionResult
{
    /// <summary>What was checked: a JSONPath, <c>header Name</c> or <c>duration</c>.</summary>
    public string Subject { get; set; } = string.Empty;
    /// <summary>The condition, e.g. <c>== "admin"</c>, <c>&gt;= 3</c>, <c>exists</c>.</summary>
    public string Condition { get; set; } = string.Empty;
    /// <summary>The actual value as JSON, or <c>(missing)</c>.</summary>
    public string ActualValue { get; set; } = string.Empty;
    public bool Passed { get; set; }
}

public class WorkflowReport
{
    public string WorkflowName { get; set; } = string.Empty;
    public List<StepReport> StepReports { get; set; } = new List<StepReport>();
}
