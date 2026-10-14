using System.Diagnostics;
using System.Reflection;
using System.Text;
using Foxify.Cli;
using Foxify.Core.Execution;
using Foxify.Core.Parsing;
using Foxify.Core.Reporting;
using Spectre.Console;

const int ExitSuccess = 0;
const int ExitWorkflowFailed = 1;
const int ExitUsageError = 2;

const string Usage = """
    Usage:
      foxify run <workflow.yaml> [options]
      foxify update [--check] [--force]
      foxify --help
      foxify --version

    Options for 'run':
      -e, --env <file>   Environment file (YAML) with initial variables, e.g. baseUrl, apiKey.
      -v, --verbose      Print variables, requests and responses while running.
      --no-color         Disable colors (also honored: the NO_COLOR environment variable).

    Options for 'update':
      --check            Only tell whether a newer version is available.
      --force            Reinstall the latest version, also over a development build.

      -h, --help         Show this help.
      --version          Show the version.

    Exit codes ('run'):
      0  all steps passed
      1  at least one step failed
      2  invalid arguments or file not found
    """;

UpdateCommand.CleanupPreviousVersion();

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

if (args[0] == "update")
{
    bool checkOnly = false, force = false, updateNoColor = false;
    foreach (var option in args.Skip(1))
    {
        switch (option)
        {
            case "--check": checkOnly = true; break;
            case "--force": force = true; break;
            case "--no-color": updateNoColor = true; break;
            default: return UsageError($"Unknown option '{option}'.");
        }
    }

    return await UpdateCommand.RunAsync(CreateConsole(updateNoColor), GetVersion(), checkOnly, force);
}

if (args[0] != "run")
{
    return UsageError($"Unknown command '{args[0]}'.");
}

string? workflowPath = null;
string? envFilePath = null;
bool verbose = false;
bool noColor = false;

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
        case "--no-color":
            noColor = true;
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

var console = CreateConsole(noColor);

try
{
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

    // In verbose mode the engine's request/response log is printed and the spinner is
    // disabled, since it would interleave with that log.
    var engine = new WorkflowEngine(httpClient, verbose ? Console.Out : TextWriter.Null);
    var reporter = new ConsoleReporter(console, live: !verbose);

    reporter.WriteHeader(GetVersion(), workflowPath, envFilePath, workflow);

    var stopwatch = Stopwatch.StartNew();
    var report = await reporter.RunAsync(engine, workflow);
    var summary = WorkflowSummary.Create(workflow, report);
    reporter.WriteSummary(summary, stopwatch.Elapsed);

    return summary.IsSuccess ? ExitSuccess : ExitWorkflowFailed;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error processing workflow: {ex.Message}");
    if (verbose)
        Console.Error.WriteLine(ex.StackTrace);
    return ExitWorkflowFailed;
}

static void PrintVariables(string title, Dictionary<string, object> variables)
{
    Console.WriteLine($"{title}:");
    foreach (var variable in variables)
    {
        Console.WriteLine($"  {variable.Key}: {variable.Value}");
    }
}

static IAnsiConsole CreateConsole(bool noColor)
{
    try
    {
        // Needed on Windows for ✓ ✗ ⚡; the reporter falls back to ASCII when Unicode is unavailable.
        Console.OutputEncoding = Encoding.UTF8;
    }
    catch (IOException)
    {
    }

    var console = AnsiConsole.Create(new AnsiConsoleSettings
    {
        ColorSystem = noColor ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect
    });

    // Without a terminal (CI logs, pipes) Spectre assumes 80 columns and wraps long lines.
    if (Console.IsOutputRedirected)
        console.Profile.Width = 240;

    return console;
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
