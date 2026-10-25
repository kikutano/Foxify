using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Tinyprobe.Cli;
using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Reporting;
using Spectre.Console;
using YamlDotNet.Core;

const int ExitSuccess = 0;
const int ExitWorkflowFailed = 1;
const int ExitUsageError = 2;

const string Usage = """
    Usage:
      tinyprobe run <workflow.yaml> [options]
      tinyprobe call <file.yaml> <endpoint> [--with name=value]... [options]
      tinyprobe inspect <file.yaml> [--json]
      tinyprobe update [--check] [--force]
      tinyprobe guide       Print the guide for coding agents (LLMs) that write tinyprobe files.
      tinyprobe --help
      tinyprobe --version

    'call' sends one request to an endpoint of an API or workflow file and prints the response.
    'inspect' validates a file and lists its endpoints, variables and steps.

    Options for 'run' and 'call':
      -e, --env <file>        Environment file (YAML) with initial variables, e.g. baseUrl, apiKey.
      --var <name=value>      Set a variable, over the environment file. Repeatable.
      --with <name=value>     ('call' only) An argument of the endpoint, like a step's 'with'. Repeatable.
      --reporter <name>       console (default) or ndjson: one JSON event per line, for tools.
      -v, --verbose           Print variables, requests and responses while running.
      --no-color              Disable colors (also honored: the NO_COLOR environment variable).

    Values of --var and --with are typed like YAML: 42 is a number, true a boolean,
    '"42"' a string, '[a, b]' a list.

    Options for 'inspect':
      --json                  Print JSON, for tools.
      --no-color              Disable colors.

    Options for 'update':
      --check                 Only tell whether a newer version is available.
      --force                 Reinstall the latest version, also over a development build.

      -h, --help              Show this help.
      --version               Show the version.

    Exit codes ('run', 'call', 'inspect'):
      0  all steps passed ('call': a response was received; 'inspect': the file is valid)
      1  at least one step failed
      2  invalid arguments, file not found or invalid file
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

if (args[0] == "guide")
    return GuideCommand.Run();

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

if (args[0] is not ("run" or "call" or "inspect"))
{
    return UsageError($"Unknown command '{args[0]}'.");
}

var command = args[0];
var positional = new List<string>();
string? envFilePath = null;
string reporterName = "console";
bool verbose = false, noColor = false, json = false;
var variables = new List<KeyValuePair<string, string>>();
var arguments = new List<KeyValuePair<string, string>>();

for (int i = 1; i < args.Length; i++)
{
    var option = args[i];
    switch (option)
    {
        case "-h" or "--help":
            Console.WriteLine(Usage);
            return ExitSuccess;
        case "--json" when command == "inspect":
            json = true;
            break;
        case "--no-color":
            noColor = true;
            break;
        case "-v" or "--verbose" when command != "inspect":
            verbose = true;
            break;
        case "-e" or "--env" or "--reporter" or "--var" when command != "inspect":
        case "--with" when command == "call":
            if (i + 1 >= args.Length)
                return UsageError($"Option '{option}' requires a value.");
            var value = args[++i];
            switch (option)
            {
                case "-e" or "--env":
                    envFilePath = value;
                    break;
                case "--reporter":
                    if (value is not ("console" or "ndjson"))
                        return UsageError($"Unknown reporter '{value}': use console or ndjson.");
                    reporterName = value;
                    break;
                default:
                    var separator = value.IndexOf('=');
                    if (separator <= 0)
                        return UsageError($"Option '{option}' expects name=value, not '{value}'.");
                    (option == "--var" ? variables : arguments).Add(new(value[..separator], value[(separator + 1)..]));
                    break;
            }
            break;
        default:
            if (option.StartsWith('-'))
                return UsageError($"Unknown option '{option}' for '{command}'.");
            positional.Add(option);
            break;
    }
}

int expectedPositional = command == "call" ? 2 : 1;
if (positional.Count < expectedPositional)
    return UsageError(command switch
    {
        "call" when positional.Count == 1 => "Missing endpoint name.",
        "run" => "Missing workflow file.",
        _ => "Missing file."
    });
if (positional.Count > expectedPositional)
    return UsageError($"Unexpected argument '{positional[expectedPositional]}'.");

var filePath = positional[0];
if (!File.Exists(filePath))
    return UsageError($"File not found: {filePath}");

if (envFilePath is not null && !File.Exists(envFilePath))
    return UsageError($"Environment file not found: {envFilePath}");

if (command == "inspect")
    return InspectCommand.Run(CreateConsole(noColor), GetVersion(), filePath, json);

var console = CreateConsole(noColor);
IRunReporter reporter = reporterName == "ndjson"
    ? new NdjsonReporter(Console.Out, GetVersion())
    // In verbose mode the engine's log is printed, so the spinner is disabled: it would interleave with it.
    : new ConsoleReporter(console, live: !verbose) { ShowResponse = command == "call" };

WorkflowDefinition workflow;
var environment = new Dictionary<string, JsonElement>();
try
{
    // Everything is validated before the first request: a typo is reported, not half-run.
    workflow = command == "call"
        ? WorkflowLoader.CreateCall(WorkflowLoader.LoadAny(filePath), positional[1], arguments)
        : WorkflowLoader.Load(filePath);
    if (envFilePath is not null)
    {
        foreach (var (name, value) in WorkflowLoader.LoadEnvironment(envFilePath))
            environment[name] = value;
    }
}
catch (WorkflowLoadException ex)
{
    reporter.WriteLoadErrors(filePath, ex.Problems);
    return ExitUsageError;
}

foreach (var (name, text) in variables)
{
    try
    {
        environment[name] = WorkflowLoader.ParseYamlValue(text);
    }
    catch (YamlException ex)
    {
        return UsageError($"Invalid value for --var {name}: {ex.Message}");
    }
}

// The ndjson stream owns stdout, so the verbose log goes to stderr there.
var log = !verbose ? TextWriter.Null : reporterName == "ndjson" ? Console.Error : Console.Out;
if (verbose)
{
    log.WriteLine("Workflow variables:");
    foreach (var (name, value) in workflow.Variables)
        log.WriteLine($"  {name}: {value}");
    log.WriteLine("Environment variables:");
    foreach (var (name, value) in environment)
        log.WriteLine($"  {name}: {value.GetRawText()}");
}

using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(workflow.Settings.Timeout) };
var engine = new WorkflowEngine(httpClient, log)
{
    RecordExchanges = reporterName == "ndjson" || command == "call"
};

reporter.WriteHeader(GetVersion(), filePath, envFilePath, workflow, environment);

var stopwatch = Stopwatch.StartNew();
var report = await reporter.RunAsync(engine, workflow, environment);
var summary = WorkflowSummary.Create(workflow, report);
reporter.WriteSummary(summary, stopwatch.Elapsed);

return summary.IsSuccess ? ExitSuccess : ExitWorkflowFailed;

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
    Console.Error.WriteLine("Run 'tinyprobe --help' for usage.");
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
