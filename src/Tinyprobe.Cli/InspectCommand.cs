using Tinyprobe.Core.Definitions;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Parsing;
using Tinyprobe.Core.Protocol;
using Spectre.Console;

namespace Tinyprobe.Cli;

/// <summary><c>tinyprobe inspect</c>: validates a workflow or API file and lists what it defines.</summary>
internal static class InspectCommand
{
    public static int Run(IAnsiConsole console, string version, string path, bool json)
    {
        WorkflowDefinition workflow;
        try
        {
            workflow = WorkflowLoader.LoadAny(path);
        }
        catch (WorkflowLoadException ex)
        {
            if (json)
                Console.WriteLine(ProtocolJson.Invalid(version, path, ex.Problems));
            else
                ConsoleReporter.WriteLoadErrors(console, ex.Errors, "file");
            return 2;
        }

        if (json)
            Console.WriteLine(ProtocolJson.Inspection(version, workflow));
        else
            Write(console, path, workflow);
        return 0;
    }

    private static void Write(IAnsiConsole console, string path, WorkflowDefinition workflow)
    {
        var isWorkflow = workflow.Steps.Count > 0;
        console.WriteLine();
        console.MarkupLine($"  [bold]{(isWorkflow ? "workflow" : "api")}[/] {Escape(workflow.Name)} [grey]{Escape(path)}[/]");

        Section(console, "endpoints", workflow.Endpoints.Count);
        int nameWidth = workflow.Endpoints.Keys.Select(k => k.Length).DefaultIfEmpty(0).Max();
        foreach (var endpoint in workflow.Endpoints.Values)
        {
            var parameters = (endpoint.Params ?? new Dictionary<string, Template?>())
                .Select(p => p.Value is null ? p.Key : $"{p.Key}?")
                .Concat(endpoint.Query.Where(q => endpoint.Params?.ContainsKey(q) != true).Select(q => $"{q}?"))
                .ToList();
            console.MarkupLine(
                $"  {Escape(endpoint.Name.PadRight(nameWidth))}  [bold]{endpoint.Method,-6}[/] {Escape(endpoint.Path.ToString())}" +
                (parameters.Count > 0 ? $"  [grey]{Escape(string.Join(", ", parameters))}[/]" : ""));
        }

        if (workflow.Variables.Count > 0)
        {
            Section(console, "variables", workflow.Variables.Count);
            foreach (var (name, value) in workflow.Variables)
                console.MarkupLine($"  {Escape(name)} [grey]=[/] {Escape(value.ToString())}");
        }

        if (isWorkflow)
        {
            Section(console, "steps", workflow.Steps.Count);
            int stepWidth = workflow.Steps.OfType<CallStep>().Select(s => s.Name.Length).DefaultIfEmpty(0).Max();
            for (int index = 0; index < workflow.Steps.Count; index++)
            {
                var text = workflow.Steps[index] switch
                {
                    CallStep call => $"{Escape(call.Name.PadRight(stepWidth))}  [grey]call {Escape(call.Call)}{(call.Until is null ? "" : ", until")}[/]",
                    SleepStep sleep => $"[grey]{sleep.Name}[/]",
                    _ => ""
                };
                console.MarkupLine($"  [grey]{index + 1,3}[/]  {text}");
            }
        }

        console.WriteLine();
        if (workflow.Endpoints.Count > 0)
            console.MarkupLine("  [grey]Required params have no '?'. Try one: tinyprobe call <file> <endpoint> --with name=value[/]");
        console.WriteLine();
    }

    private static void Section(IAnsiConsole console, string title, int count)
    {
        console.WriteLine();
        console.MarkupLine($"  [orange1]{title}[/] [grey]({count})[/]");
    }

    private static string Escape(string value) => Markup.Escape(value);
}
