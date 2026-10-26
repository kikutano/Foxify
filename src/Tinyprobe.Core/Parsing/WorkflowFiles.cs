using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Tinyprobe.Core.Parsing;

/// <summary>A path given to <c>tinyprobe run</c> cannot be used: a usage error, exit code 2.</summary>
public sealed class WorkflowFilesException(string message) : Exception(message);

/// <summary>
/// Resolves the paths given to <c>tinyprobe run</c> (workflow files and folders) to the workflow files to run.
/// </summary>
public static class WorkflowFiles
{
    /// <summary>
    /// Folders are read recursively, in ordinal order of the path; files are kept in the order given.
    /// A file reached twice is listed once.
    /// </summary>
    public static IReadOnlyList<string> Find(IReadOnlyList<string> paths)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var workflows = YamlFiles(path).Where(IsWorkflow).ToList();
                if (workflows.Count == 0)
                    throw new WorkflowFilesException($"No workflow files in {path} (a workflow has a top-level 'steps' key).");
                foreach (var workflow in workflows)
                    Add(workflow);
            }
            else if (File.Exists(path))
            {
                if (!IsWorkflow(path))
                    throw new WorkflowFilesException(
                        $"{path} is not a workflow (it has no 'steps'): run a workflow that imports it, or 'tinyprobe call' one endpoint.");
                Add(path);
            }
            else
            {
                throw new WorkflowFilesException($"File not found: {path}");
            }
        }

        return found;

        void Add(string file)
        {
            if (seen.Add(Path.GetFullPath(file)))
                found.Add(file);
        }
    }

    /// <summary>
    /// A workflow is a YAML file with a top-level <c>steps</c> key. A file that is not valid YAML counts as one,
    /// so that its error is reported instead of the file being skipped without a word.
    /// </summary>
    public static bool IsWorkflow(string path)
    {
        try
        {
            var stream = new YamlStream();
            using (var reader = new StreamReader(path))
                stream.Load(reader);

            return stream.Documents.Count > 0
                && stream.Documents[0].RootNode is YamlMappingNode root
                && root.Children.ContainsKey(new YamlScalarNode("steps"));
        }
        catch (YamlException)
        {
            return true;
        }
    }

    private static IEnumerable<string> YamlFiles(string folder)
    {
        // API files, env files and workflows often share a folder: everything is read, and IsWorkflow picks.
        var files = new List<string>();
        Collect(folder);
        files.Sort(StringComparer.Ordinal);
        return files;

        void Collect(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var extension = Path.GetExtension(file);
                if (extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
                    files.Add(file);
            }

            // .git, .dev, .vscode and the like are not part of a test suite.
            foreach (var subdirectory in Directory.EnumerateDirectories(directory))
            {
                if (!Path.GetFileName(subdirectory).StartsWith('.'))
                    Collect(subdirectory);
            }
        }
    }
}
