using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace Foxify.Core.Definitions;

/// <summary>Where something is defined, for error messages: <c>file:line:column</c>.</summary>
public readonly record struct SourceLocation(string File, int Line, int Column)
{
    public override string ToString() => $"{File}:{Line}:{Column}";
}

/// <summary>
/// A YAML value that may contain <c>${name}</c> placeholders, resolved when the step runs.
/// </summary>
public sealed class Template
{
    internal Template(YamlNode node, SourceLocation location)
    {
        Node = node;
        Location = location;
    }

    internal YamlNode Node { get; }

    public SourceLocation Location { get; }

    public override string ToString() => Node is YamlScalarNode scalar ? scalar.Value ?? string.Empty : Node.ToString();
}

/// <summary>A workflow ready to run: its steps, with every <c>call</c> bound to an endpoint.</summary>
public sealed class WorkflowDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, EndpointDefinition> Endpoints { get; init; } = new Dictionary<string, EndpointDefinition>();
    public IReadOnlyList<StepDefinition> Steps { get; init; } = [];
    /// <summary>Default variables; the environment file overrides them.</summary>
    public IReadOnlyDictionary<string, JsonElement> Variables { get; init; } = new Dictionary<string, JsonElement>();
    public WorkflowSettings Settings { get; init; } = new();
}

public sealed class WorkflowSettings
{
    /// <summary>Timeout of each HTTP request, in seconds.</summary>
    public int Timeout { get; init; } = 30;
    /// <summary>Retries on network errors and timeouts; HTTP error status codes are never retried.</summary>
    public int MaxRetries { get; init; }
}

public sealed class EndpointDefinition
{
    public required string Name { get; init; }
    public string Method { get; init; } = "GET";
    public Template? BaseUrl { get; init; }
    public required Template Path { get; init; }
    /// <summary>Query parameters, sent only when the step passes them (or a param default exists).</summary>
    public IReadOnlyList<string> Query { get; init; } = [];
    /// <summary>Headers in the order they apply: file headers, auth sets, endpoint headers. Later ones win.</summary>
    public IReadOnlyList<KeyValuePair<string, Template>> Headers { get; init; } = [];
    public Template? Body { get; init; }
    /// <summary>Declared params with their default (null = required), or null when the endpoint declares none.</summary>
    public IReadOnlyDictionary<string, Template?>? Params { get; init; }
    public required SourceLocation Location { get; init; }
}

public abstract class StepDefinition
{
    public required SourceLocation Location { get; init; }
}

public sealed class SleepStep : StepDefinition
{
    public required TimeSpan Duration { get; init; }
}

public sealed class CallStep : StepDefinition
{
    /// <summary>The step's <c>name</c>, or a default made of the call and its path/query arguments.</summary>
    public string Name { get; internal set; } = string.Empty;
    internal bool HasExplicitName { get; init; }
    public required string Call { get; init; }
    public IReadOnlyDictionary<string, Template> With { get; init; } = new Dictionary<string, Template>();
    public Expectation Expect { get; init; } = new();
    /// <summary>Variable name to JSONPath of the response body.</summary>
    public IReadOnlyDictionary<string, string> Save { get; init; } = new Dictionary<string, string>();
    /// <summary>The endpoint named by <see cref="Call"/>, bound when the workflow is loaded.</summary>
    public EndpointDefinition Endpoint { get; internal set; } = null!;
}

public sealed class Expectation
{
    /// <summary>Accepted status codes; when empty any 2xx is accepted.</summary>
    public IReadOnlyList<int> Status { get; init; } = [];
    public IReadOnlyList<Check> Headers { get; init; } = [];
    public IReadOnlyList<Check> Body { get; init; } = [];
    public IReadOnlyList<Condition> Duration { get; init; } = [];
}

/// <summary>Conditions on one header (by name) or one body value (by JSONPath).</summary>
public sealed class Check
{
    public required string Subject { get; init; }
    public required IReadOnlyList<Condition> Conditions { get; init; }
}

public enum Operator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    Exists,
    Contains,
    /// <summary>Equal to one of the values of a list.</summary>
    In,
    /// <summary>At least one of the <see cref="Condition.Alternatives"/> holds.</summary>
    Any
}

public sealed class Condition
{
    public required Operator Operator { get; init; }
    /// <summary>The expected value; null for <see cref="Operator.Exists"/> and duration conditions.</summary>
    public Template? Value { get; init; }
    /// <summary>For <see cref="Operator.Exists"/>: whether the value must exist.</summary>
    public bool Exists { get; init; }
    /// <summary>For duration conditions.</summary>
    public TimeSpan? DurationValue { get; init; }
    /// <summary>For <see cref="Operator.Any"/>: each alternative is a list of conditions that must all hold.</summary>
    public IReadOnlyList<IReadOnlyList<Condition>> Alternatives { get; init; } = [];
}
