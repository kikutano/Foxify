using System.Text.RegularExpressions;
using Foxify.Core.Parsing;

namespace Foxify.Core.Tests.Docs;

/// <summary>
/// The YAML examples of the documentation must stay valid: coding agents copy them
/// (docs/llm-guide.md is printed by <c>foxify guide</c>), so a stale example teaches wrong syntax.
/// </summary>
public sealed partial class DocumentationTests : IDisposable
{
    [GeneratedRegex(@"```yaml\r?\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex YamlBlock();

    [GeneratedRegex(@"^# file: (\S+)")]
    private static partial Regex FileMarker();

    private readonly string _directory = Directory.CreateTempSubdirectory("foxify-docs-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Foxify.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, relativePath);
    }

    private static List<string> YamlBlocks(string markdownPath) =>
        YamlBlock().Matches(File.ReadAllText(RepositoryFile(markdownPath))).Select(m => m.Groups[1].Value).ToList();

    /// <summary>Writes the blocks that start with "# file: path" to their path, so imports resolve.</summary>
    private List<string> WriteMarkedFiles(string markdownPath)
    {
        var written = new List<string>();
        foreach (var block in YamlBlocks(markdownPath))
        {
            var marker = FileMarker().Match(block);
            if (!marker.Success)
                continue;

            var path = Path.Combine(_directory, marker.Groups[1].Value);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, block);
            written.Add(path);
        }
        return written;
    }

    private static bool IsWorkflow(string yaml) => Regex.IsMatch(yaml, @"^steps:", RegexOptions.Multiline);

    private static void AssertLoads(Action load, string what)
    {
        try
        {
            load();
        }
        catch (WorkflowLoadException ex)
        {
            Assert.Fail($"{what} is not valid:{Environment.NewLine}{string.Join(Environment.NewLine, ex.Errors)}");
        }
    }

    [Fact]
    public void LlmGuide_CompleteExamples_AreValid()
    {
        var files = WriteMarkedFiles("docs/llm-guide.md");

        var workflows = files.Where(f => IsWorkflow(File.ReadAllText(f))).ToList();
        var environments = files.Where(f => Path.GetFileName(f).StartsWith("env.", StringComparison.Ordinal)).ToList();

        // Guards the extraction itself: an example API file, two workflows and two env files.
        Assert.Equal(2, workflows.Count);
        Assert.Equal(2, environments.Count);
        Assert.Equal(5, files.Count);

        foreach (var workflow in workflows)
            AssertLoads(() => WorkflowLoader.Load(workflow), Path.GetFileName(workflow));
        foreach (var environment in environments)
            AssertLoads(() => WorkflowLoader.LoadEnvironment(environment), Path.GetFileName(environment));
    }

    [Fact]
    public void LlmGuide_StandaloneExamples_AreValid()
    {
        // Blocks without a file marker that are whole workflows (with their own endpoints) must load too.
        var standalone = YamlBlocks("docs/llm-guide.md")
            .Where(b => !FileMarker().IsMatch(b) && IsWorkflow(b) && !b.Contains("import:"))
            .ToList();

        foreach (var block in standalone)
            AssertLoads(() => WorkflowLoader.Parse(block, Path.Combine(_directory, "snippet.yaml")), "a guide snippet");
    }

    [Fact]
    public void Readme_StandaloneWorkflows_AreValid()
    {
        var standalone = YamlBlocks("README.md").Where(b => IsWorkflow(b) && !b.Contains("import:")).ToList();

        Assert.NotEmpty(standalone);
        foreach (var block in standalone)
            AssertLoads(() => WorkflowLoader.Parse(block, Path.Combine(_directory, "readme.yaml")), "a README workflow");
    }
}
