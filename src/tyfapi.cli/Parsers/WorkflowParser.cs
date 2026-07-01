using TyfApi.Cli.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TyfApi.Cli.Parsers;

public class WorkflowParser
{
    private readonly IDeserializer _deserializer;

    public WorkflowParser()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    public WorkflowTemplate Parse(string yamlContent)
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

        return result;
    }
}