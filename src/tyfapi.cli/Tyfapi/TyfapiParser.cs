using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Text.Json;

namespace tyfapi.cli.Tyfapi;

public class TyfapiParser
{
    private readonly IDeserializer _deserializer;

    public TyfapiParser()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    public WorkflowTemplate Parse(string yamlContent, Dictionary<string, object> environmentVariables = null)
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
}
