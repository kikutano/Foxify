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
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    }

    /// <summary>
    /// Parses a YAML string into a WorkflowTemplate object.
    /// </summary>
    /// <param name="yamlContent">The raw YAML content of the workflow file.</param>
    /// <returns>A populated WorkflowTemplate instance.</returns>
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

/*</write_to_file>
<task_progress>
- [x] Explore project structure
- [x] Define models based on YAML schema
- [ ] Implement parsing logic (no reflection)
- [ ] Write unit test
</task_progress>
</write_to_file>*/