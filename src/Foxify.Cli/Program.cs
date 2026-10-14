using System.Reflection;
using Foxify.Core.Execution;
using Foxify.Core.Models;
using Foxify.Core.Parsing;

const int ExitSuccess = 0;
const int ExitWorkflowFailed = 1;
const int ExitUsageError = 2;

const string Usage = """
    Usage:
      foxify run <workflow.yaml> [options]
      foxify --help
      foxify --version

    Options:
      -e, --env <file>   Environment file (YAML) with initial variables, e.g. baseUrl, apiKey.
      -v, --verbose      Print the variables loaded before execution.
      -h, --help         Show this help.
      --version          Show the version.

    Exit codes:
      0  all steps passed
      1  at least one step failed
      2  invalid arguments or file not found
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? ExitUsageError : ExitSuccess;
}

if (args[0] is "--version")
{
    Console.WriteLine(GetVersion());
    return ExitSuccess;
}

if (args[0] != "run")
{
    return UsageError($"Unknown command '{args[0]}'.");
}

string? workflowPath = null;
string? envFilePath = null;
bool verbose = false;

for (int i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-e" or "--env":
            if (i + 1 >= args.Length)
                return UsageError($"Option '{args[i]}' requires a file path.");
            envFilePath = args[++i];
            break;
        case "-v" or "--verbose":
            verbose = true;
            break;
        case "-h" or "--help":
            Console.WriteLine(Usage);
            return ExitSuccess;
        default:
            if (args[i].StartsWith('-'))
                return UsageError($"Unknown option '{args[i]}'.");
            if (workflowPath is not null)
                return UsageError($"Unexpected argument '{args[i]}'.");
            workflowPath = args[i];
            break;
    }
}

if (workflowPath is null)
    return UsageError("Missing workflow file.");

if (!File.Exists(workflowPath))
    return UsageError($"Workflow file not found: {workflowPath}");

if (envFilePath is not null && !File.Exists(envFilePath))
    return UsageError($"Environment file not found: {envFilePath}");

try
{
    Console.WriteLine($"Loading workflow from: {workflowPath}");

    var parser = new WorkflowParser();
    var environmentVariables = envFilePath is null
        ? new Dictionary<string, object>()
        : parser.ParseEnvironment(File.ReadAllText(envFilePath));

    var workflow = parser.Parse(File.ReadAllText(workflowPath), environmentVariables);

    if (verbose)
    {
        PrintVariables("Metadata variables", workflow.Metadata.Variables);
        PrintVariables("Workflow variables", workflow.Variables);
        PrintVariables("Environment variables", workflow.EnvironmentVariables);
    }

    using var httpClient = new HttpClient();
    if (workflow.Settings?.Timeout > 0)
        httpClient.Timeout = TimeSpan.FromSeconds(workflow.Settings.Timeout);

    using var engine = new WorkflowEngine(httpClient);
    var report = await engine.ExecuteWorkflowAsync(workflow);

    return PrintSummary(report);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error processing workflow: {ex.Message}");
    if (verbose)
        Console.Error.WriteLine(ex.StackTrace);
    return ExitWorkflowFailed;
}

static int PrintSummary(WorkflowReport report)
{
    int failed = report.StepReports.Count(s => !s.IsSuccess);
    int passed = report.StepReports.Count - failed;
    long totalMs = report.StepReports.Sum(s => s.ExecutionTimeMilliseconds);

    Console.WriteLine();
    Console.WriteLine($"Workflow: {report.WorkflowName}");
    foreach (var step in report.StepReports)
    {
        Console.WriteLine($"  {(step.IsSuccess ? "PASS" : "FAIL")}  {step.StepName} ({step.ExecutionTimeMilliseconds} ms)");
    }
    Console.WriteLine($"{passed} passed, {failed} failed, {totalMs} ms");

    return failed == 0 ? ExitSuccess : ExitWorkflowFailed;
}

static void PrintVariables(string title, Dictionary<string, object> variables)
{
    Console.WriteLine($"{title}:");
    foreach (var variable in variables)
    {
        Console.WriteLine($"  {variable.Key}: {variable.Value}");
    }
}

static int UsageError(string message)
{
    Console.Error.WriteLine($"Error: {message}");
    Console.Error.WriteLine("Run 'foxify --help' for usage.");
    return ExitUsageError;
}

static string GetVersion()
{
    var informational = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion;

    // Strip the "+<commit sha>" suffix the SDK appends to the informational version.
    return informational?.Split('+')[0] ?? "unknown";
}
