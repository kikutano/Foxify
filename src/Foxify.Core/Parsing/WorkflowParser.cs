using Foxify.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Foxify.Core.Parsing;

public class WorkflowParser
{
    private readonly IDeserializer _deserializer;

    public WorkflowParser()
    {
        _deserializer = new StaticDeserializerBuilder(new FoxifyYamlContext())
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    public WorkflowTemplate Parse(string yamlContent, Dictionary<string, object>? environmentVariables = null)
    {
        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            throw new ArgumentException("YAML content cannot be empty.", nameof(yamlContent));
        }

        var result = _deserializer.Deserialize<WorkflowTemplate>(yamlContent);

        if (result == null)
        {
            throw new InvalidOperationException("Failed to deserialize the workflow YAML content.");
        }

        // Add environment variables to the workflow template if provided
        if (environmentVariables != null)
        {
            foreach (var envVar in environmentVariables)
            {
                result.EnvironmentVariables[envVar.Key] = envVar.Value;
            }
        }

        return result;
    }

    /// <summary>
    /// Parses an environment file: a flat YAML map of variable names to values (e.g. baseUrl, apiKey).
    /// </summary>
    public Dictionary<string, object> ParseEnvironment(string yamlContent)
    {
        if (string.IsNullOrWhiteSpace(yamlContent))
            return new();

        return _deserializer.Deserialize<Dictionary<string, object>>(yamlContent) ?? new();
    }
}
