using System.Diagnostics;
using System.Text.Json;
using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Reporting;

namespace Tinyprobe.Cli;

/// <summary><c>tinyprobe run</c> on a folder or several files: every workflow, then one summary.</summary>
internal static class RunCommand
{
    /// <param name="target">The first path of the command line, named by the <c>invalid</c> event.</param>
    /// <param name="createHandler">A new HTTP handler for each workflow.</param>
    public static async Task<int> RunSuiteAsync(
        IRunReporter reporter, string version, string target, IReadOnlyList<string> files,
        string? envPath, IReadOnlyDictionary<string, JsonElement> environment,
        TextWriter log, bool recordExchanges, Func<HttpMessageHandler> createHandler, string? junitPath = null)
    {
        var (workflows, problems) = LoadSuite(files);
        if (problems.Count > 0)
        {
            reporter.WriteSuiteLoadErrors(target, problems);
            if (junitPath is not null)
                WriteJunit(junitPath, JunitReport.WriteInvalid(version, problems));
            return 2;
        }

        reporter.WriteSuiteStart(version, files);
        var suiteStopwatch = Stopwatch.StartNew();
        var entries = new List<SuiteEntry>();
        var junitRuns = new List<JunitRun>();

        foreach (var (file, workflow) in workflows)
        {
            // A new client and handler per workflow: each one starts from zero, cookies and connections
            // included (decision 0003), and gets its own timeout setting.
            using var httpClient = new HttpClient(createHandler()) { Timeout = TimeSpan.FromSeconds(workflow.Settings.Timeout) };
            var engine = new WorkflowEngine(httpClient, log) { RecordExchanges = recordExchanges };

            reporter.WriteHeader(version, file, envPath, workflow, environment);
            var started = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var report = await reporter.RunAsync(engine, workflow, environment);
            var entry = SuiteEntry.Create(file, workflow, report, stopwatch.Elapsed);
            reporter.WriteSummary(entry.Summary, stopwatch.Elapsed);
            entries.Add(entry);
            junitRuns.Add(new JunitRun(file, workflow, report, stopwatch.Elapsed, started));
        }

        var suite = new SuiteSummary(entries);
        reporter.WriteSuiteSummary(suite, suiteStopwatch.Elapsed);
        if (junitPath is not null && !WriteJunit(junitPath, JunitReport.Write(version, junitRuns)))
            return 2;
        return suite.IsSuccess ? 0 : 1;
    }

    /// <summary>Writes the JUnit report, creating its folder; false (and a message) when it cannot be written.</summary>
    public static bool WriteJunit(string path, string xml)
    {
        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } directory)
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, xml);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Error: Cannot write the JUnit report {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads every workflow before the first request, as for one workflow: a typo in the last file must not
    /// show up after the first files have already changed the backend's data.
    /// </summary>
    public static (List<(string File, WorkflowDefinition Workflow)> Workflows, List<LoadProblem> Problems) LoadSuite(
        IReadOnlyList<string> files)
    {
        var workflows = new List<(string File, WorkflowDefinition Workflow)>();
        var problems = new List<LoadProblem>();
        foreach (var file in files)
        {
            try
            {
                workflows.Add((file, WorkflowLoader.Load(file)));
            }
            catch (WorkflowLoadException ex)
            {
                problems.AddRange(ex.Problems);
            }
        }
        return (workflows, problems);
    }
}
