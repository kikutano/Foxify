namespace TyfApi.Cli.Models;

public record WorkflowMetadata(
    string Name,
    string Description,
    string Environment,
    string ApiVersion
);

public record FunctionDefinition(
    string Type,
    string Method,
    string Endpoint,
    Dictionary<string, string> Headers,
    string Body,
    Dictionary<string, string> Extract
);

public record WorkflowStep(
    string? Name,
    string Type,
    string? FunctionName,
    List<string>? DependsOn,
    double? DurationSeconds
);

public record WorkflowSettings(
    int Timeout,
    int MaxRetries
);

public record WorkflowTemplate(
    WorkflowMetadata Metadata,
    Dictionary<string, FunctionDefinition> Functions,
    List<WorkflowStep> Workflow,
    WorkflowSettings Settings
);