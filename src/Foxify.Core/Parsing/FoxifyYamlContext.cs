using Foxify.Core.Models;
using YamlDotNet.Serialization;

namespace Foxify.Core.Parsing;

/// <summary>
/// Source-generated YAML type metadata. Required for Native AOT, where the
/// reflection-based YamlDotNet deserializer gets trimmed away.
/// Every model reachable from a YAML document must be listed here.
/// </summary>
[YamlStaticContext]
[YamlSerializable(typeof(WorkflowTemplate))]
[YamlSerializable(typeof(WorkflowMetadata))]
[YamlSerializable(typeof(FunctionDefinition))]
[YamlSerializable(typeof(WorkflowStep))]
[YamlSerializable(typeof(WorkflowSettings))]
public partial class FoxifyYamlContext : StaticContext
{
}
