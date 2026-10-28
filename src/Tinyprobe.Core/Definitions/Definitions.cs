using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace Tinyprobe.Core.Definitions;

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
    /// <summary>
    /// Default variables; the environment file overrides them. Each one is resolved once per run,
    /// the first time it is used, so a generated value (<c>${$uuid}</c>) stays the same in every step.
    /// </summary>
    public IReadOnlyDictionary<string, Template> Variables { get; init; } = new Dictionary<string, Template>();
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
    /// <summary><c>form</c>: an <c>application/x-www-form-urlencoded</c> body, the fields in order. At most one of body, form, multipart.</summary>
    public IReadOnlyList<BodyField>? Form { get; init; }
    /// <summary><c>multipart</c>: a <c>multipart/form-data</c> body, the fields in order.</summary>
    public IReadOnlyList<BodyField>? Multipart { get; init; }
    /// <summary>Declared params with their default (null = required), or null when the endpoint declares none.</summary>
    public IReadOnlyDictionary<string, Template?>? Params { get; init; }
    public required SourceLocation Location { get; init; }
}

/// <summary>
/// One field of a <c>form</c> or <c>multipart</c> body: a text <see cref="Value"/>, or a <see cref="File"/> to send.
/// A list in YAML is already one field per item, under the same name.
/// </summary>
public sealed class BodyField
{
    public required string Name { get; init; }
    public Template? Value { get; init; }
    /// <summary>The path of the file, relative to the file tinyprobe was given (the workflow, or the API file of a call).</summary>
    public Template? File { get; init; }
    public Template? FileName { get; init; }
    public Template? ContentType { get; init; }

    internal IEnumerable<Template> Templates => new[] { Value, File, FileName, ContentType }.OfType<Template>();
}

public abstract class StepDefinition
{
    public required SourceLocation Location { get; init; }
}

public sealed class SleepStep : StepDefinition
{
    public required TimeSpan Duration { get; init; }

    /// <summary>How the step is shown: <c>sleep 500ms</c>.</summary>
    public string Name => $"sleep {Execution.Durations.Format(Duration)}";
}

public sealed class CallStep : StepDefinition
{
    /// <summary>The step's <c>name</c>, or a default made of the call and its path/query arguments.</summary>
    public string Name { get; internal set; } = string.Empty;
    internal bool HasExplicitName { get; init; }
    public required string Call { get; init; }
    public IReadOnlyDictionary<string, Template> With { get; init; } = new Dictionary<string, Template>();
    public Expectation Expect { get; init; } = new();
    /// <summary>When set, the call is repeated until these checks pass (polling), then <see cref="Expect"/> is evaluated.</summary>
    public RetryUntil? Until { get; init; }
    /// <summary>Variable name to where its value is read: a JSONPath of the response body, or a cookie.</summary>
    public IReadOnlyDictionary<string, SaveSource> Save { get; init; } = new Dictionary<string, SaveSource>();
    /// <summary>The endpoint named by <see cref="Call"/>, bound when the workflow is loaded.</summary>
    public EndpointDefinition Endpoint { get; internal set; } = null!;
}

/// <summary>Where a <c>save</c> value is read: a JSONPath of the response body, or (<c>{ cookie: name }</c>) a cookie in the jar.</summary>
public sealed record SaveSource(string? Path = null, string? Cookie = null)
{
    /// <summary>How it is shown, in <c>inspect --json</c> too: <c>$.id</c> or <c>cookie XSRF-TOKEN</c>.</summary>
    public override string ToString() => Cookie is null ? Path ?? string.Empty : $"cookie {Cookie}";
}

/// <summary><c>until</c>: repeat a call every <see cref="Every"/> until <see cref="Checks"/> pass, for at most <see cref="Timeout"/>.</summary>
public sealed class RetryUntil
{
    public static readonly TimeSpan DefaultEvery = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Status, header, cookie and body checks; duration checks are not used.</summary>
    public Expectation Checks { get; init; } = new();
    public TimeSpan Every { get; init; } = DefaultEvery;
    public TimeSpan Timeout { get; init; } = DefaultTimeout;
}

public sealed class Expectation
{
    /// <summary>Accepted status codes; when empty any 2xx is accepted.</summary>
    public IReadOnlyList<int> Status { get; init; } = [];
    /// <summary>Accept any status, also 4xx and 5xx: set by <c>tinyprobe call</c>, which explores an endpoint.</summary>
    public bool AnyStatus { get; init; }
    public IReadOnlyList<Check> Headers { get; init; } = [];
    /// <summary>Cookies by name, read from the jar after the response, for the request's URL.</summary>
    public IReadOnlyList<Check> Cookies { get; init; } = [];
    public IReadOnlyList<Check> Body { get; init; } = [];
    public IReadOnlyList<Condition> Duration { get; init; } = [];
}

/// <summary>Conditions on one header or cookie (by name) or one body value (by JSONPath).</summary>
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
