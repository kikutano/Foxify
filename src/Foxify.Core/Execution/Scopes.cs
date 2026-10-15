using System.Text.Json;
using Foxify.Core.Definitions;

namespace Foxify.Core.Execution;

internal interface IVariableScope
{
    bool TryGet(string name, out JsonElement value);
}

/// <summary>Workflow-wide variables: saved values, then the environment, then the workflow defaults.</summary>
internal sealed class GlobalVariables(
    IReadOnlyDictionary<string, JsonElement> defaults,
    IReadOnlyDictionary<string, JsonElement> environment) : IVariableScope
{
    private readonly Dictionary<string, JsonElement> _saved = new();

    public void Save(string name, JsonElement value) => _saved[name] = value;

    public bool TryGet(string name, out JsonElement value) =>
        _saved.TryGetValue(name, out value)
        || environment.TryGetValue(name, out value)
        || defaults.TryGetValue(name, out value);
}

/// <summary>
/// Variables seen by one call of an endpoint. Declared params are local: they come from the
/// step's <c>with</c> or their default, never from global variables. Any other name is looked
/// up in the step's <c>with</c> first, then in the globals.
/// </summary>
internal sealed class CallScope : IVariableScope
{
    private readonly Dictionary<string, JsonElement> _arguments;
    private readonly Dictionary<string, JsonElement> _params = new();
    private readonly IVariableScope _globals;

    public CallScope(EndpointDefinition endpoint, Dictionary<string, JsonElement> arguments, IVariableScope globals)
    {
        _arguments = arguments;
        _globals = globals;

        foreach (var (name, defaultValue) in endpoint.Params ?? new Dictionary<string, Template?>())
        {
            if (arguments.TryGetValue(name, out var argument))
                _params[name] = argument;
            else if (defaultValue is not null)
                _params[name] = TemplateRenderer.RenderValue(defaultValue, globals);
            else
                throw new InvalidOperationException($"Missing required param '{name}' of endpoint '{endpoint.Name}'.");
        }

        DeclaredParams = endpoint.Params?.Keys.ToHashSet() ?? [];
    }

    private HashSet<string> DeclaredParams { get; }

    public bool TryGet(string name, out JsonElement value)
    {
        if (DeclaredParams.Contains(name))
            return _params.TryGetValue(name, out value);

        return _arguments.TryGetValue(name, out value) || _globals.TryGet(name, out value);
    }

    /// <summary>A value passed by the step or a param default, ignoring globals (used for query parameters).</summary>
    public bool TryGetArgument(string name, out JsonElement value) =>
        DeclaredParams.Contains(name)
            ? _params.TryGetValue(name, out value)
            : _arguments.TryGetValue(name, out value);
}
