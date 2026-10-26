using Tinyprobe.Core.Parsing;

namespace Tinyprobe.Core.Tests.Parsing;

public sealed class WorkflowFilesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tinyprobe-files-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private const string Workflow = "name: w\nsteps:\n  - call: ping\n";

    private string Relative(string path) => Path.GetRelativePath(_directory, path).Replace('\\', '/');

    [Fact]
    public void Folder_IsReadRecursively_InOrdinalOrder_KeepingOnlyWorkflows()
    {
        Write("tinyprobe/workflows/users.yaml", Workflow);
        Write("tinyprobe/workflows/auth.yml", Workflow);
        Write("tinyprobe/workflows/admin/Zones.yaml", Workflow);
        Write("tinyprobe/api/backend.yaml", "endpoints:\n  ping: { path: /ping }\n");
        Write("tinyprobe/env.local.yaml", "baseUrl: http://localhost\n");
        Write("tinyprobe/MIGRATION.md", "# not yaml\n");
        Write("tinyprobe/.cache/old.yaml", Workflow);

        var files = WorkflowFiles.Find([Path.Combine(_directory, "tinyprobe")]);

        Assert.Equal(
            ["tinyprobe/workflows/admin/Zones.yaml", "tinyprobe/workflows/auth.yml", "tinyprobe/workflows/users.yaml"],
            files.Select(Relative));
    }

    [Fact]
    public void Files_KeepTheOrderGiven_AndRunOnce()
    {
        var b = Write("b.yaml", Workflow);
        var a = Write("a.yaml", Workflow);

        var files = WorkflowFiles.Find([b, a, _directory, Path.Combine(_directory, "./b.yaml")]);

        Assert.Equal(["b.yaml", "a.yaml"], files.Select(Relative));
    }

    [Fact]
    public void InvalidYaml_CountsAsAWorkflow_SoItsErrorIsReported()
    {
        Write("broken.yaml", "steps: [\n");

        Assert.Equal(["broken.yaml"], WorkflowFiles.Find([_directory]).Select(Relative));
    }

    [Fact]
    public void FolderWithoutWorkflows_IsAnError()
    {
        Write("api/backend.yaml", "endpoints:\n  ping: { path: /ping }\n");
        var folder = Path.Combine(_directory, "api");

        var ex = Assert.Throws<WorkflowFilesException>(() => WorkflowFiles.Find([folder]));

        Assert.Equal($"No workflow files in {folder} (a workflow has a top-level 'steps' key).", ex.Message);
    }

    [Fact]
    public void ApiFileOnTheCommandLine_IsAnError()
    {
        var api = Write("backend.yaml", "endpoints:\n  ping: { path: /ping }\n");

        var ex = Assert.Throws<WorkflowFilesException>(() => WorkflowFiles.Find([Write("w.yaml", Workflow), api]));

        Assert.Equal(
            $"{api} is not a workflow (it has no 'steps'): run a workflow that imports it, or 'tinyprobe call' one endpoint.",
            ex.Message);
    }

    [Fact]
    public void MissingPath_IsAnError()
    {
        var missing = Path.Combine(_directory, "nope");

        var ex = Assert.Throws<WorkflowFilesException>(() => WorkflowFiles.Find([missing]));

        Assert.Equal($"File not found: {missing}", ex.Message);
    }
}
