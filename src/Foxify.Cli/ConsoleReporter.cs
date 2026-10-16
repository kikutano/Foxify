using System.Globalization;
using System.Text.Json;
using Foxify.Core.Definitions;
using Foxify.Core.Execution;
using Foxify.Core.Models;
using Foxify.Core.Reporting;
using Spectre.Console;

namespace Foxify.Cli;

/// <summary>
/// Renders a workflow run: a header, one line per step as it completes (with a spinner
/// while a request is in flight on interactive terminals) and a k6-style summary.
/// </summary>
internal sealed class ConsoleReporter
{
    private const int MaxNameWidth = 32;
    private const int MetricNameWidth = 20;

    private readonly IAnsiConsole _console;
    private readonly bool _live;
    private readonly bool _unicode;
    private readonly string _pass;
    private readonly string _fail;
    private int _nameWidth = MaxNameWidth;

    /// <param name="live">Show a spinner while steps run; ignored when the output is not interactive (CI, pipes).</param>
    public ConsoleReporter(IAnsiConsole console, bool live)
    {
        _console = console;
        _live = live && console.Profile.Capabilities.Interactive;
        _unicode = console.Profile.Capabilities.Unicode;
        _pass = _unicode ? "✓" : "v";
        _fail = _unicode ? "✗" : "x";
    }

    public void WriteHeader(string version, string workflowPath, string? envPath,
        WorkflowDefinition workflow, IReadOnlyDictionary<string, JsonElement> environment)
    {
        var steps = workflow.Steps.OfType<CallStep>().ToList();
        _nameWidth = Math.Clamp(steps.Select(s => s.Name.Length).DefaultIfEmpty(0).Max(), 4, MaxNameWidth);

        _console.WriteLine();
        _console.MarkupLine($"  [bold orange1]{(_unicode ? "⚡ " : "")}foxify[/] [grey]{Escape(version)}[/]");
        _console.WriteLine();

        WriteHeaderRow("workflow", workflow.Name);
        WriteHeaderRow("file", workflowPath);
        if (envPath is not null)
            WriteHeaderRow("env", envPath);
        if (environment.TryGetValue("baseUrl", out var baseUrl) && baseUrl.ValueKind == JsonValueKind.String)
            WriteHeaderRow("base url", baseUrl.GetString()!);
        WriteHeaderRow("steps", steps.Count.ToString(CultureInfo.InvariantCulture));

        _console.WriteLine();
    }

    /// <summary>Prints the problems found while loading the workflow, one per line with its location.</summary>
    public static void WriteLoadErrors(IAnsiConsole console, IReadOnlyList<string> errors)
    {
        var fail = console.Profile.Capabilities.Unicode ? "✗" : "x";
        console.WriteLine();
        console.MarkupLine($"  [bold red]{fail} The workflow is not valid[/] [grey]({errors.Count} {(errors.Count == 1 ? "problem" : "problems")})[/]");
        console.WriteLine();
        foreach (var error in errors)
            console.MarkupLine($"  [red]{Escape(error)}[/]");
        console.WriteLine();
    }

    public async Task<WorkflowReport> RunAsync(
        WorkflowEngine engine, WorkflowDefinition workflow, IReadOnlyDictionary<string, JsonElement> environment)
    {
        engine.StepCompleted += WriteStep;

        if (!_live)
            return await engine.ExecuteAsync(workflow, environment);

        WorkflowReport? report = null;
        await _console.Status()
            .Spinner(_unicode ? Spinner.Known.Dots : Spinner.Known.Ascii)
            .SpinnerStyle(new Style(Color.Orange1))
            .StartAsync("starting", async context =>
            {
                engine.StepStarted += name => context.Status($"[grey]running[/] {Escape(name)}");
                report = await engine.ExecuteAsync(workflow, environment);
            });

        return report!;
    }

    public void WriteSummary(WorkflowSummary summary, TimeSpan elapsed)
    {
        _console.WriteLine();

        int checks = summary.ChecksPassed + summary.ChecksFailed;
        WriteMetric("checks", checks == 0
            ? "[grey]none[/]"
            : $"{100.0 * summary.ChecksPassed / checks:0.00}%  [green]{_pass} {summary.ChecksPassed}[/]  {Count(_fail, summary.ChecksFailed)}");

        WriteMetric("steps", $"{summary.PassedSteps}/{summary.TotalSteps} passed"
            + (summary.FailedSteps > 0 ? $"  [red]{summary.FailedSteps} failed[/]" : "")
            + (summary.NotRunSteps > 0 ? $"  [yellow]{summary.NotRunSteps} not run[/]" : ""));

        var rate = elapsed.TotalSeconds > 0 ? summary.HttpRequests / elapsed.TotalSeconds : 0;
        WriteMetric("http_reqs", $"{summary.HttpRequests}  [grey]{rate:0.##}/s[/]");

        if (summary.HttpRequestDuration is { } d)
        {
            WriteMetric("http_req_duration",
                $"avg={Value(d.Average)} min={Value(d.Min)} med={Value(d.Median)} " +
                $"max={Value(d.Max)} p(90)={Value(d.P90)} p(95)={Value(d.P95)}");
        }

        WriteMetric("data_received", FormatBytes(summary.BytesReceived));
        WriteMetric("data_sent", FormatBytes(summary.BytesSent));
        WriteMetric("workflow_duration", FormatDuration(elapsed));

        _console.WriteLine();
        _console.MarkupLine(summary.IsSuccess
            ? $"  [bold black on green] PASSED [/]  {summary.TotalSteps} steps"
            : $"  [bold white on red] FAILED [/]  {summary.FailedSteps} failed"
              + (summary.NotRunSteps > 0 ? $", {summary.NotRunSteps} not run" : ""));
        _console.WriteLine();
    }

    private void WriteStep(StepReport step)
    {
        var icon = step.IsSuccess ? $"[green]{_pass}[/]" : $"[red]{_fail}[/]";
        var name = Escape(Truncate(step.StepName, _nameWidth).PadRight(_nameWidth));
        var method = Escape((step.Method ?? "").PadRight(6));
        var duration = step.StatusCode is null ? "" : FormatDuration(step.Duration);
        var path = step.Url is null ? "" : step.Url.IsAbsoluteUri ? step.Url.PathAndQuery : step.Url.ToString();
        var attempts = step.Attempts > 1 ? $"  [grey]({step.Attempts} attempts)[/]" : "";

        _console.MarkupLine(
            $"  {icon} {(step.IsSuccess ? name : $"[red]{name}[/]")}  [bold]{method}[/] {StatusMarkup(step)}" +
            $"  [grey]{duration,9}  {Escape(path)}[/]{attempts}");

        if (step.IsSuccess)
            return;

        if (step.FailureReason is not null)
            _console.MarkupLine($"      [red]{_fail} {Escape(step.FailureReason)}[/]");

        if (step.Assertions.Any(a => !a.Passed))
        {
            foreach (var assertion in step.Assertions)
            {
                var check = $"{Escape(assertion.Subject)} {Escape(assertion.Condition)}";
                _console.MarkupLine(assertion.Passed
                    ? $"      [green]{_pass}[/] [grey]{check}[/]"
                    : $"      [red]{_fail}[/] {check}  [grey]actual[/] [red]{Escape(assertion.ActualValue)}[/]");
            }
        }
    }

    private void WriteHeaderRow(string label, string value) =>
        _console.MarkupLine($"  [grey]{label,-8}[/] : {Escape(value)}");

    private void WriteMetric(string name, string valueMarkup) =>
        _console.MarkupLine($"  {name}[grey]{new string('.', Math.Max(1, MetricNameWidth - name.Length))}[/]: {valueMarkup}");

    private string Value(TimeSpan duration) => $"[cyan]{FormatDuration(duration)}[/]";

    private static string Count(string icon, int count) =>
        count > 0 ? $"[red]{icon} {count}[/]" : $"[grey]{icon} {count}[/]";

    private static string StatusMarkup(StepReport step)
    {
        if (step.StatusCode is null)
            return "[grey]---[/]";

        int code = (int)step.StatusCode;
        var color = code switch
        {
            < 300 => "green",
            < 400 => "cyan",
            < 500 => "yellow",
            _ => "red"
        };
        return $"[{color}]{code}[/]";
    }

    private string FormatDuration(TimeSpan duration) => duration.TotalMilliseconds switch
    {
        < 1 => $"{duration.TotalMicroseconds:0}{(_unicode ? "µs" : "us")}",
        < 1000 => $"{duration.TotalMilliseconds:0.##}ms",
        _ => $"{duration.TotalSeconds:0.##}s"
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1000 => $"{bytes} B",
        < 1_000_000 => $"{bytes / 1000.0:0.#} kB",
        _ => $"{bytes / 1_000_000.0:0.#} MB"
    };

    private string Truncate(string value, int width) =>
        value.Length <= width ? value : value[..(width - 1)] + (_unicode ? "…" : "~");

    private static string Escape(string value) => Markup.Escape(value);
}
