using System.Text.Json;
using System.Text.RegularExpressions;
using Foxify.Core.Definitions;
using Foxify.Core.Execution;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Foxify.Core.Parsing;

/// <summary>One problem of a file; <see cref="Line"/> is 0 when it concerns the whole file.</summary>
public sealed record LoadProblem(string File, int Line, int Column, string Message)
{
    public override string ToString() => Line > 0 ? $"{File}:{Line}:{Column}: {Message}" : $"{File}: {Message}";
}

/// <summary>A workflow, API or environment file is invalid; <see cref="Problems"/> lists every problem found.</summary>
public sealed class WorkflowLoadException(IReadOnlyList<LoadProblem> problems)
    : Exception(string.Join(Environment.NewLine, problems))
{
    public IReadOnlyList<LoadProblem> Problems { get; } = problems;

    /// <summary>The problems as <c>file:line:column: message</c>.</summary>
    public IReadOnlyList<string> Errors { get; } = problems.Select(p => p.ToString()).ToList();
}

/// <summary>
/// Loads workflow files (and the API files they import) and environment files.
/// The whole workflow is validated before anything runs; problems are reported as
/// <c>file:line:column: message</c>.
/// </summary>
public static class WorkflowLoader
{
    public static WorkflowDefinition Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return new Loader().LoadWorkflow(File.ReadAllText(fullPath), fullPath);
    }

    /// <summary>Loads a workflow from text; <paramref name="filePath"/> names it in errors and anchors relative imports.</summary>
    public static WorkflowDefinition Parse(string yaml, string filePath) =>
        new Loader().LoadWorkflow(yaml, Path.GetFullPath(filePath));

    /// <summary>
    /// Loads a workflow, or an API file (a file without <c>steps</c>) as a workflow with no steps:
    /// what <c>foxify inspect</c> and <c>foxify call</c> accept.
    /// </summary>
    public static WorkflowDefinition LoadAny(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return new Loader().LoadAny(File.ReadAllText(fullPath), fullPath);
    }

    /// <summary>
    /// A workflow of one step that calls <paramref name="endpoint"/> of <paramref name="source"/> with
    /// <paramref name="arguments"/> (values as YAML text, see <see cref="ParseYamlValue"/>) and accepts any status.
    /// The arguments are validated like a step's <c>with</c>.
    /// </summary>
    public static WorkflowDefinition CreateCall(
        WorkflowDefinition source, string endpoint, IReadOnlyList<KeyValuePair<string, string>> arguments) =>
        new Loader().CreateCall(source, endpoint, arguments);

    /// <summary>Loads an environment file: a flat map of variable names to values.</summary>
    public static IReadOnlyDictionary<string, JsonElement> LoadEnvironment(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return new Loader().LoadEnvironment(File.ReadAllText(fullPath), fullPath);
    }

    /// <summary>
    /// A value given on the command line (<c>--var id=42</c>), typed like a YAML value: <c>42</c> is a number,
    /// <c>true</c> a boolean, <c>"42"</c> a string, <c>[a, b]</c> a list; an empty value is an empty string.
    /// Placeholders are not resolved, as in an environment file.
    /// </summary>
    public static JsonElement ParseYamlValue(string text) => TemplateRenderer.ToLiteral(Loader.ParseValueNode(text));
}

internal sealed partial class Loader
{
    private static readonly string[] ApiKeys = ["base_url", "headers", "auth", "endpoints"];
    private static readonly string[] WorkflowKeys = ["name", "description", "import", "variables", "settings", "steps", .. ApiKeys];
    private static readonly string[] EndpointKeys = ["method", "path", "query", "headers", "auth", "body", "params"];
    private static readonly string[] StepKeys = ["name", "call", "with", "expect", "until", "save", "sleep"];
    private static readonly string[] ExpectKeys = ["status", "headers", "body", "duration"];
    private static readonly string[] UntilKeys = ["status", "headers", "body", "every", "timeout"];
    private static readonly string[] SettingsKeys = ["timeout", "max_retries"];

    private static readonly Dictionary<string, Operator> Operators = new()
    {
        ["eq"] = Operator.Equal,
        ["ne"] = Operator.NotEqual,
        ["gt"] = Operator.GreaterThan,
        ["gte"] = Operator.GreaterOrEqual,
        ["lt"] = Operator.LessThan,
        ["lte"] = Operator.LessOrEqual,
        ["exists"] = Operator.Exists,
        ["contains"] = Operator.Contains,
        ["in"] = Operator.In,
        ["any"] = Operator.Any
    };

    private static readonly string[] DurationOperators = ["gt", "gte", "lt", "lte"];

    [GeneratedRegex(@"^[A-Za-z]+$")]
    private static partial Regex HttpMethod();

    private readonly List<(SourceLocation Location, string Message)> _problems = new();
    private readonly List<string> _files = new();

    private sealed record FileContext(string Display, string Directory);

    public WorkflowDefinition LoadWorkflow(string text, string fullPath)
    {
        var root = Open(text, fullPath, out var file);
        if (root is null)
            throw Problems();

        var entries = Collect(file, root, "workflow", WorkflowKeys);

        var endpoints = new Dictionary<string, EndpointDefinition>();
        var imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (entries.TryGetValue("import", out var imports))
        {
            foreach (var item in Items(file, imports, "import"))
            {
                if (Scalar(file, item, "an import") is not { } importPath)
                    continue;

                var importFullPath = Path.GetFullPath(Path.Combine(file.Directory, importPath));
                if (!File.Exists(importFullPath))
                {
                    Error(file, item, $"imported file not found: {importPath}");
                    continue;
                }

                if (imported.Add(importFullPath))
                    AddEndpoints(endpoints, LoadApiFile(importFullPath));
            }
        }

        // A workflow can also define its own endpoints, handy for small single-file tests.
        AddEndpoints(endpoints, ParseApi(file, entries));

        var steps = entries.TryGetValue("steps", out var stepsNode) ? ParseSteps(file, stepsNode) : [];
        if (steps.Count == 0 && _problems.Count == 0)
            Error(file, root, "the workflow has no steps");

        foreach (var step in steps.OfType<CallStep>())
            Bind(step, endpoints);

        var workflow = new WorkflowDefinition
        {
            Name = entries.TryGetValue("name", out var name) ? Scalar(file, name, "name") ?? "" : Path.GetFileNameWithoutExtension(fullPath),
            Description = entries.TryGetValue("description", out var description) ? Scalar(file, description, "description") ?? "" : "",
            SourceFile = fullPath,
            Endpoints = endpoints,
            Steps = steps,
            Variables = entries.TryGetValue("variables", out var variables) ? ParseTemplates(file, variables, "variables") : new Dictionary<string, Template>(),
            Settings = entries.TryGetValue("settings", out var settings) ? ParseSettings(file, settings) : new WorkflowSettings()
        };

        ValidateGeneratedValues(endpoints.Values, steps.OfType<CallStep>(), workflow.Variables.Values);

        if (_problems.Count > 0)
            throw Problems();

        return workflow;
    }

    public WorkflowDefinition LoadAny(string text, string fullPath)
    {
        var root = Open(text, fullPath, out var file);
        if (root is null)
            throw Problems();

        if (root.Children.Keys.Any(k => k is YamlScalarNode { Value: "steps" }))
            return LoadWorkflow(text, fullPath);

        var endpoints = new Dictionary<string, EndpointDefinition>();
        AddEndpoints(endpoints, ParseApi(file, Collect(file, root, "an API file", ApiKeys)));
        if (_problems.Count > 0)
            throw Problems();

        return new WorkflowDefinition
        {
            Name = Path.GetFileNameWithoutExtension(fullPath),
            SourceFile = fullPath,
            Endpoints = endpoints
        };
    }

    public WorkflowDefinition CreateCall(
        WorkflowDefinition source, string endpoint, IReadOnlyList<KeyValuePair<string, string>> arguments)
    {
        var location = new SourceLocation("command line", 0, 0);
        _files.Add(location.File);

        var with = new Dictionary<string, Template>();
        foreach (var (name, text) in arguments)
        {
            try
            {
                with[name] = new Template(ParseValueNode(text), location);
            }
            catch (YamlException ex)
            {
                Report(location, $"invalid value for '{name}': {ex.Message}");
            }
        }

        var step = new CallStep { Call = endpoint, With = with, Expect = new Expectation { AnyStatus = true }, Location = location };
        Bind(step, source.Endpoints.ToDictionary());
        ValidateGeneratedValues([], [step], []);

        if (_problems.Count > 0)
            throw Problems();

        return new WorkflowDefinition
        {
            Name = source.Name,
            Description = source.Description,
            SourceFile = source.SourceFile,
            Endpoints = source.Endpoints,
            Steps = [step],
            Variables = source.Variables,
            Settings = source.Settings
        };
    }

    /// <summary>A command-line value as a YAML node; an empty value is an empty string, not null.</summary>
    internal static YamlNode ParseValueNode(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        return stream.Documents.Count == 0
            ? new YamlScalarNode(text) { Style = ScalarStyle.DoubleQuoted }
            : stream.Documents[0].RootNode;
    }

    public IReadOnlyDictionary<string, JsonElement> LoadEnvironment(string text, string fullPath)
    {
        var root = Open(text, fullPath, out var file);
        var values = root is null ? new Dictionary<string, JsonElement>() : ParseLiterals(file, root, "the environment file");

        if (_problems.Count > 0)
            throw Problems();

        return values;
    }

    // --- Files ---------------------------------------------------------------

    private YamlMappingNode? Open(string text, string fullPath, out FileContext file)
    {
        file = new FileContext(DisplayPath(fullPath), Path.GetDirectoryName(fullPath)!);
        if (!_files.Contains(file.Display))
            _files.Add(file.Display);

        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException ex)
        {
            Report(new SourceLocation(file.Display, (int)ex.Start.Line, (int)ex.Start.Column), ex.Message);
            return null;
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            Report(new SourceLocation(file.Display, 0, 0), "expected a YAML mapping at the top level");
            return null;
        }

        return root;
    }

    private List<EndpointDefinition> LoadApiFile(string fullPath)
    {
        var root = Open(File.ReadAllText(fullPath), fullPath, out var file);
        return root is null ? [] : ParseApi(file, Collect(file, root, "an API file", ApiKeys));
    }

    private void AddEndpoints(Dictionary<string, EndpointDefinition> endpoints, IEnumerable<EndpointDefinition> added)
    {
        foreach (var endpoint in added)
        {
            if (endpoints.TryGetValue(endpoint.Name, out var existing))
                Report(endpoint.Location, $"endpoint '{endpoint.Name}' is already defined at {existing.Location}");
            else
                endpoints[endpoint.Name] = endpoint;
        }
    }

    // --- API definitions -----------------------------------------------------

    private List<EndpointDefinition> ParseApi(FileContext file, Dictionary<string, YamlNode> entries)
    {
        var baseUrl = entries.TryGetValue("base_url", out var baseUrlNode) && Scalar(file, baseUrlNode, "base_url") is not null
            ? Template(file, baseUrlNode)
            : null;
        var fileHeaders = entries.TryGetValue("headers", out var headersNode) ? ParseHeaders(file, headersNode, "headers") : [];

        var authSets = new Dictionary<string, List<KeyValuePair<string, Template>>>();
        if (entries.TryGetValue("auth", out var authNode))
        {
            foreach (var (name, _, value) in Entries(file, authNode, "auth"))
                authSets[name] = ParseHeaders(file, value, $"auth '{name}'");
        }

        var endpoints = new List<EndpointDefinition>();
        if (entries.TryGetValue("endpoints", out var endpointsNode))
        {
            foreach (var (name, keyNode, value) in Entries(file, endpointsNode, "endpoints"))
            {
                if (ParseEndpoint(file, name, keyNode, value, baseUrl, fileHeaders, authSets) is { } endpoint)
                    endpoints.Add(endpoint);
            }
        }

        return endpoints;
    }

    private EndpointDefinition? ParseEndpoint(
        FileContext file, string name, YamlNode keyNode, YamlNode node, Template? baseUrl,
        List<KeyValuePair<string, Template>> fileHeaders, Dictionary<string, List<KeyValuePair<string, Template>>> authSets)
    {
        var what = $"endpoint '{name}'";
        var entries = Collect(file, node, what, EndpointKeys);

        var method = "GET";
        if (entries.TryGetValue("method", out var methodNode) && Scalar(file, methodNode, "method") is { } methodText)
        {
            if (HttpMethod().IsMatch(methodText))
                method = methodText.ToUpperInvariant();
            else
                Error(file, methodNode, $"invalid HTTP method '{methodText}'");
        }

        if (!entries.TryGetValue("path", out var pathNode) || Scalar(file, pathNode, "path") is null)
        {
            if (pathNode is null)
                Error(file, keyNode, $"{what} has no path");
            return null;
        }

        var query = new List<string>();
        if (entries.TryGetValue("query", out var queryNode))
        {
            foreach (var item in Items(file, queryNode, "query"))
            {
                if (Scalar(file, item, "a query parameter") is { } parameter)
                    query.Add(parameter);
            }
        }

        var headers = new List<KeyValuePair<string, Template>>(fileHeaders);
        if (entries.TryGetValue("auth", out var authNode))
        {
            var names = authNode is YamlSequenceNode ? Items(file, authNode, "auth") : [authNode];
            foreach (var item in names)
            {
                if (Scalar(file, item, "auth") is not { } authName)
                    continue;
                if (authSets.TryGetValue(authName, out var authHeaders))
                    headers.AddRange(authHeaders);
                else
                    Error(file, item, $"unknown auth '{authName}'{Suggest(authName, authSets.Keys)}");
            }
        }

        if (entries.TryGetValue("headers", out var headersNode))
            headers.AddRange(ParseHeaders(file, headersNode, $"headers of {what}"));

        Dictionary<string, Template?>? parameters = null;
        if (entries.TryGetValue("params", out var paramsNode))
        {
            parameters = new Dictionary<string, Template?>();
            foreach (var (paramName, _, value) in Entries(file, paramsNode, "params"))
                parameters[paramName] = IsNull(value) ? null : Template(file, value);
        }

        return new EndpointDefinition
        {
            Name = name,
            Method = method,
            BaseUrl = baseUrl,
            Path = Template(file, pathNode),
            Query = query,
            Headers = headers,
            Body = entries.TryGetValue("body", out var bodyNode) ? Template(file, bodyNode) : null,
            Params = parameters,
            Location = At(file, keyNode)
        };
    }

    private List<KeyValuePair<string, Template>> ParseHeaders(FileContext file, YamlNode node, string what)
    {
        var headers = new List<KeyValuePair<string, Template>>();
        foreach (var (name, _, value) in Entries(file, node, what))
        {
            if (Scalar(file, value, $"header '{name}'") is not null)
                headers.Add(new(name, Template(file, value)));
        }
        return headers;
    }

    // --- Steps ---------------------------------------------------------------

    private List<StepDefinition> ParseSteps(FileContext file, YamlNode node)
    {
        var steps = new List<StepDefinition>();
        foreach (var item in Items(file, node, "steps"))
        {
            var entries = Collect(file, item, "a step", StepKeys);
            var location = At(file, item);

            if (entries.TryGetValue("sleep", out var sleepNode))
            {
                foreach (var key in new[] { "call", "with", "expect", "until", "save" }.Where(entries.ContainsKey))
                    Error(file, item, $"a sleep step cannot have '{key}'");

                if (Scalar(file, sleepNode, "sleep") is { } text)
                {
                    if (Durations.TryParse(text, out var duration))
                        steps.Add(new SleepStep { Duration = duration, Location = location });
                    else
                        Error(file, sleepNode, $"invalid duration '{text}': use e.g. 500ms, 2s or 1m");
                }
                continue;
            }

            if (!entries.TryGetValue("call", out var callNode))
            {
                if (entries.Count > 0 || item is not YamlMappingNode)
                    Error(file, item, "a step needs 'call' (or 'sleep')");
                continue;
            }

            if (Scalar(file, callNode, "call") is not { } call)
                continue;

            var with = new Dictionary<string, Template>();
            if (entries.TryGetValue("with", out var withNode))
            {
                foreach (var (name, _, value) in Entries(file, withNode, "with"))
                    with[name] = Template(file, value);
            }

            var save = new Dictionary<string, string>();
            if (entries.TryGetValue("save", out var saveNode))
            {
                foreach (var (name, _, value) in Entries(file, saveNode, "save"))
                {
                    if (Scalar(file, value, $"save '{name}'") is not { } path)
                        continue;
                    if (JsonPath.IsValid(path))
                        save[name] = path;
                    else
                        Error(file, value, $"invalid JSONPath '{path}': use e.g. $.id or $.data[0].email");
                }
            }

            steps.Add(new CallStep
            {
                Name = entries.TryGetValue("name", out var nameNode) ? Scalar(file, nameNode, "name") ?? call : call,
                HasExplicitName = nameNode is not null,
                Call = call,
                With = with,
                Save = save,
                Expect = entries.TryGetValue("expect", out var expectNode) ? ParseExpect(file, expectNode) : new Expectation(),
                Until = entries.TryGetValue("until", out var untilNode) ? ParseUntil(file, untilNode) : null,
                Location = location
            });
        }

        return steps;
    }

    /// <summary>
    /// An unnamed step is shown as its call plus the arguments that go into the path or query,
    /// e.g. <c>status code=200</c>. Body and header arguments are left out: they may be secrets.
    /// </summary>
    private static string DefaultName(CallStep step, EndpointDefinition endpoint)
    {
        var visible = TemplateRenderer.Placeholders(endpoint.Path.Node).Concat(endpoint.Query).ToHashSet();
        var arguments = step.With
            .Where(a => visible.Contains(a.Key) && a.Value.Node is YamlScalarNode)
            .Select(a => $"{a.Key}={a.Value}");
        var name = string.Join(' ', [step.Call, .. arguments]);
        return name.Length <= 40 ? name : step.Call;
    }

    private Expectation ParseExpect(FileContext file, YamlNode node)
    {
        var entries = Collect(file, node, "expect", ExpectKeys);
        var checks = ParseChecks(file, entries, "expect");
        var duration = entries.TryGetValue("duration", out var durationNode) ? ParseDurationConditions(file, durationNode) : [];

        return new Expectation { Status = checks.Status, Headers = checks.Headers, Body = checks.Body, Duration = duration };
    }

    /// <summary><c>until</c>: the same status, header and body checks as <c>expect</c>, plus <c>every</c> and <c>timeout</c>.</summary>
    private RetryUntil? ParseUntil(FileContext file, YamlNode node)
    {
        var entries = Collect(file, node, "until", UntilKeys);
        if (!entries.Keys.Any(k => k is "status" or "headers" or "body"))
        {
            Error(file, node, "until needs a condition: status, headers or body");
            return null;
        }

        var every = ParseDuration(file, entries, "every", RetryUntil.DefaultEvery);
        var timeout = ParseDuration(file, entries, "timeout", RetryUntil.DefaultTimeout);
        if (every <= TimeSpan.Zero)
            Error(file, entries["every"], "every must be greater than 0");
        else if (timeout < every)
            Error(file, entries.GetValueOrDefault("timeout") ?? node, $"timeout ({Durations.Format(timeout)}) must be at least every ({Durations.Format(every)})");

        return new RetryUntil { Checks = ParseChecks(file, entries, "until"), Every = every, Timeout = timeout };
    }

    private TimeSpan ParseDuration(FileContext file, Dictionary<string, YamlNode> entries, string key, TimeSpan fallback)
    {
        if (!entries.TryGetValue(key, out var node) || Scalar(file, node, key) is not { } text)
            return fallback;
        if (Durations.TryParse(text, out var duration))
            return duration;

        Error(file, node, $"invalid duration '{text}': use e.g. 500ms, 2s or 1m");
        return fallback;
    }

    private Expectation ParseChecks(FileContext file, Dictionary<string, YamlNode> entries, string what)
    {
        // `status: 200` or `status: [200, 204]`: any of the listed codes is accepted.
        var status = new List<int>();
        if (entries.TryGetValue("status", out var statusNode))
        {
            var codes = statusNode is YamlSequenceNode ? Items(file, statusNode, "status").ToList() : [statusNode];
            if (codes.Count == 0)
                Error(file, statusNode, "status needs at least one code");

            foreach (var codeNode in codes)
            {
                if (Scalar(file, codeNode, "a status code") is not { } statusText)
                    continue;
                if (int.TryParse(statusText, out var code) && code is >= 100 and <= 599)
                    status.Add(code);
                else
                    Error(file, codeNode, $"invalid status code '{statusText}'");
            }
        }

        var headers = new List<Check>();
        if (entries.TryGetValue("headers", out var headersNode))
        {
            foreach (var (name, _, value) in Entries(file, headersNode, $"{what}.headers"))
                headers.Add(new Check { Subject = name, Conditions = ParseConditions(file, value) });
        }

        var body = new List<Check>();
        if (entries.TryGetValue("body", out var bodyNode))
        {
            foreach (var (path, keyNode, value) in Entries(file, bodyNode, $"{what}.body"))
            {
                if (JsonPath.IsValid(path))
                    body.Add(new Check { Subject = path, Conditions = ParseConditions(file, value) });
                else
                    Error(file, keyNode, $"invalid JSONPath '{path}': use e.g. $.id or $.data[0].email");
            }
        }

        return new Expectation { Status = status, Headers = headers, Body = body };
    }

    /// <summary>
    /// A plain value means equality; a mapping made only of operators (<c>{ gte: 3 }</c>) is a list of conditions.
    /// </summary>
    private List<Condition> ParseConditions(FileContext file, YamlNode node)
    {
        if (node is YamlMappingNode mapping && mapping.Children.Count > 0)
        {
            var keys = mapping.Children.Keys.Select(k => (k as YamlScalarNode)?.Value ?? "").ToList();
            var operatorCount = keys.Count(Operators.ContainsKey);

            if (operatorCount > 0 && operatorCount < keys.Count)
            {
                Error(file, node, "cannot mix operators (gte, exists, ...) with object fields; to compare an object use eq: { ... }");
                return [];
            }

            if (operatorCount == keys.Count)
            {
                var conditions = new List<Condition>();
                foreach (var (key, value) in mapping.Children)
                {
                    var op = Operators[((YamlScalarNode)key).Value!];
                    switch (op)
                    {
                        case Operator.Exists:
                            if (value is YamlScalarNode { Value: "true" or "false" } flag)
                                conditions.Add(new Condition { Operator = op, Exists = flag.Value == "true" });
                            else
                                Error(file, value, "exists must be true or false");
                            break;

                        case Operator.In:
                            if (value is YamlSequenceNode { Children.Count: > 0 })
                                conditions.Add(new Condition { Operator = op, Value = Template(file, value) });
                            else
                                Error(file, value, "in needs a list of values, e.g. { in: [active, pending] }");
                            break;

                        case Operator.Any:
                            // Each alternative is a value (equality) or a mapping of operators (all must hold).
                            if (value is YamlSequenceNode { Children.Count: > 0 } alternatives)
                                conditions.Add(new Condition { Operator = op, Alternatives = alternatives.Children.Select(a => ParseConditions(file, a)).ToList() });
                            else
                                Error(file, value, "any needs a list of conditions, e.g. { any: [ { lt: 0 }, { gt: 100 } ] }");
                            break;

                        default:
                            conditions.Add(new Condition { Operator = op, Value = Template(file, value) });
                            break;
                    }
                }
                return conditions;
            }
        }

        return [new Condition { Operator = Operator.Equal, Value = Template(file, node) }];
    }

    private List<Condition> ParseDurationConditions(FileContext file, YamlNode node)
    {
        var conditions = new List<Condition>();
        if (node is not YamlMappingNode mapping || mapping.Children.Count == 0)
        {
            Error(file, node, "duration needs a comparison, e.g. { lt: 500ms }");
            return conditions;
        }

        foreach (var (name, keyNode, value) in Entries(file, mapping, "expect.duration", DurationOperators))
        {
            if (Scalar(file, value, "a duration") is not { } text)
                continue;
            if (Durations.TryParse(text, out var limit))
                conditions.Add(new Condition { Operator = Operators[name], DurationValue = limit });
            else
                Error(file, value, $"invalid duration '{text}': use e.g. 500ms, 2s or 1m");
        }

        return conditions;
    }

    private void Bind(CallStep step, Dictionary<string, EndpointDefinition> endpoints)
    {
        if (!endpoints.TryGetValue(step.Call, out var endpoint))
        {
            Report(step.Location, $"unknown endpoint '{step.Call}'{Suggest(step.Call, endpoints.Keys)}");
            return;
        }

        step.Endpoint = endpoint;
        if (!step.HasExplicitName)
            step.Name = DefaultName(step, endpoint);

        foreach (var (name, defaultValue) in endpoint.Params ?? new Dictionary<string, Template?>())
        {
            if (defaultValue is null && !step.With.ContainsKey(name))
                Report(step.Location, $"missing required param '{name}' of endpoint '{endpoint.Name}' (defined at {endpoint.Location})");
        }

        // An argument nothing reads is almost always a typo (`pagesize` for `pageSize`): it would
        // silently send nothing, and the step could still pass.
        var accepted = AcceptedArguments(step, endpoint);
        foreach (var (name, value) in step.With)
        {
            if (!accepted.Contains(name))
                Report(value.Location, $"unknown argument '{name}' for endpoint '{endpoint.Name}'{Suggest(name, accepted)}");
        }
    }

    /// <summary>
    /// Names a step can pass in <c>with</c>: the endpoint's params and query names, every placeholder
    /// of its base URL, path, headers (auth sets included) and body, and the placeholders of the
    /// step's own expectations.
    /// </summary>
    private static HashSet<string> AcceptedArguments(CallStep step, EndpointDefinition endpoint)
    {
        var names = EndpointTemplates(endpoint).Concat(CheckTemplates(step))
            .SelectMany(t => TemplateRenderer.Placeholders(t.Node))
            .ToHashSet();
        names.UnionWith(endpoint.Query);
        names.UnionWith(endpoint.Params?.Keys ?? []);
        return names;
    }

    /// <summary>Names a call of the endpoint can pass, sorted; generated values excluded.</summary>
    internal static List<string> EndpointArguments(EndpointDefinition endpoint)
    {
        var names = EndpointTemplates(endpoint).SelectMany(t => TemplateRenderer.Placeholders(t.Node)).ToHashSet();
        names.UnionWith(endpoint.Query);
        names.UnionWith(endpoint.Params?.Keys ?? []);
        return names.Where(n => !Generators.IsGenerated(n)).Order(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<Template> EndpointTemplates(EndpointDefinition endpoint) =>
        new[] { endpoint.BaseUrl, endpoint.Path, endpoint.Body }
            .Concat(endpoint.Headers.Select(h => h.Value))
            .Concat(endpoint.Params?.Values ?? [])
            .OfType<Template>();

    /// <summary>Expected values of the step's <c>expect</c> and <c>until</c> checks.</summary>
    private static IEnumerable<Template> CheckTemplates(CallStep step)
    {
        var checks = step.Expect.Headers.Concat(step.Expect.Body);
        if (step.Until is { } until)
            checks = checks.Concat(until.Checks.Headers).Concat(until.Checks.Body);
        return checks.SelectMany(c => Values(c.Conditions)).OfType<Template>();

        static IEnumerable<Template?> Values(IEnumerable<Condition> conditions) =>
            conditions.SelectMany(c => c.Alternatives.SelectMany(Values).Append(c.Value));
    }

    /// <summary>Every <c>${$...}</c> placeholder must be a known generator: a typo like <c>${$uid}</c> is reported.</summary>
    private void ValidateGeneratedValues(
        IEnumerable<EndpointDefinition> endpoints, IEnumerable<CallStep> steps, IEnumerable<Template> variables)
    {
        var templates = endpoints.SelectMany(EndpointTemplates)
            .Concat(steps.SelectMany(s => s.With.Values.Concat(CheckTemplates(s))))
            .Concat(variables);

        foreach (var template in templates)
        {
            foreach (var name in TemplateRenderer.Placeholders(template.Node).Where(Generators.IsGenerated).Distinct())
            {
                if (Generators.Validate(name) is { } problem)
                    Report(template.Location, $"{problem}{Suggest(name, Generators.Names)}");
            }
        }
    }

    // --- Values and settings -------------------------------------------------

    private Dictionary<string, Template> ParseTemplates(FileContext file, YamlNode node, string what)
    {
        var values = new Dictionary<string, Template>();
        foreach (var (name, _, value) in Entries(file, node, what))
            values[name] = Template(file, value);
        return values;
    }

    private Dictionary<string, JsonElement> ParseLiterals(FileContext file, YamlNode node, string what)
    {
        var values = new Dictionary<string, JsonElement>();
        foreach (var (name, _, value) in Entries(file, node, what))
            values[name] = TemplateRenderer.ToLiteral(value);
        return values;
    }

    private WorkflowSettings ParseSettings(FileContext file, YamlNode node)
    {
        var entries = Collect(file, node, "settings", SettingsKeys);
        int timeout = 30, maxRetries = 0;

        if (entries.TryGetValue("timeout", out var timeoutNode) && Scalar(file, timeoutNode, "timeout") is { } timeoutText
            && !(int.TryParse(timeoutText, out timeout) && timeout > 0))
            Error(file, timeoutNode, $"timeout must be a positive number of seconds, not '{timeoutText}'");

        if (entries.TryGetValue("max_retries", out var retriesNode) && Scalar(file, retriesNode, "max_retries") is { } retriesText
            && !(int.TryParse(retriesText, out maxRetries) && maxRetries >= 0))
            Error(file, retriesNode, $"max_retries must be zero or more, not '{retriesText}'");

        return new WorkflowSettings { Timeout = Math.Max(timeout, 1), MaxRetries = Math.Max(maxRetries, 0) };
    }

    // --- YAML helpers --------------------------------------------------------

    private static SourceLocation At(FileContext file, YamlNode node) =>
        new(file.Display, (int)node.Start.Line, (int)node.Start.Column);

    private static Template Template(FileContext file, YamlNode node) => new(node, At(file, node));

    private void Error(FileContext file, YamlNode node, string message) => Report(At(file, node), message);

    private void Report(SourceLocation location, string message) => _problems.Add((location, message));

    /// <summary>All problems, ordered by file (in the order the files were read) and position.</summary>
    private WorkflowLoadException Problems() => new(_problems
        .OrderBy(p => _files.IndexOf(p.Location.File))
        .ThenBy(p => p.Location.Line)
        .ThenBy(p => p.Location.Column)
        .Select(p => new LoadProblem(p.Location.File, p.Location.Line, p.Location.Column, p.Message))
        .ToList());

    /// <summary>A path relative to the current directory when the file is inside it, otherwise the full path.</summary>
    private static string DisplayPath(string fullPath)
    {
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? fullPath : relative;
    }

    private static bool IsNull(YamlNode node) =>
        node is YamlScalarNode { Style: ScalarStyle.Plain or ScalarStyle.Any, Value: null or "" or "~" or "null" or "Null" or "NULL" };

    /// <summary>The entries of a mapping, by key; unknown keys are reported (with a suggestion) and skipped.</summary>
    private Dictionary<string, YamlNode> Collect(FileContext file, YamlNode node, string what, string[] allowed) =>
        Entries(file, node, what, allowed).ToDictionary(e => e.Key, e => e.Value);

    private IEnumerable<(string Key, YamlNode KeyNode, YamlNode Value)> Entries(
        FileContext file, YamlNode node, string what, string[]? allowed = null)
    {
        if (node is not YamlMappingNode mapping)
        {
            if (!IsNull(node))
                Error(file, node, $"{what} must be a mapping (key: value)");
            yield break;
        }

        foreach (var (keyNode, value) in mapping.Children)
        {
            if (keyNode is not YamlScalarNode { Value: { } key })
            {
                Error(file, keyNode, $"keys of {what} must be text");
                continue;
            }

            if (allowed is not null && !allowed.Contains(key))
            {
                Error(file, keyNode, $"unknown key '{key}' in {what}{Suggest(key, allowed)}");
                continue;
            }

            yield return (key, keyNode, value);
        }
    }

    private IEnumerable<YamlNode> Items(FileContext file, YamlNode node, string what)
    {
        if (node is YamlSequenceNode sequence)
            return sequence.Children;

        if (!IsNull(node))
            Error(file, node, $"{what} must be a list");
        return [];
    }

    private string? Scalar(FileContext file, YamlNode node, string what)
    {
        if (node is YamlScalarNode scalar)
            return scalar.Value ?? string.Empty;

        Error(file, node, $"{what} must be a single value");
        return null;
    }

    /// <summary>" (did you mean 'x'?)" for the closest candidate, or nothing when none is close.</summary>
    private static string Suggest(string value, IEnumerable<string> candidates)
    {
        var best = candidates
            .Select(c => (Candidate: c, Distance: Distance(value.ToLowerInvariant(), c.ToLowerInvariant())))
            .OrderBy(c => c.Distance)
            .FirstOrDefault();

        return best.Candidate is not null && best.Distance <= Math.Max(2, value.Length / 3)
            ? $" (did you mean '{best.Candidate}'?)"
            : string.Empty;
    }

    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            previous = current;
        }
        return previous[b.Length];
    }
}
