using System.Net;
using System.Text;
using System.Text.Json;
using Tinyprobe.Core.Execution;
using Tinyprobe.Core.Models;
using Tinyprobe.Core.Parsing;

namespace Tinyprobe.Core.Tests.Fakes;

public sealed record CapturedRequest(
    string Method, string Url, IReadOnlyDictionary<string, string> Headers, string? ContentType, string? Body)
{
    public string Path => new Uri(Url).AbsolutePath;
}

public sealed record FakeResponse(HttpStatusCode Status = HttpStatusCode.OK, string Body = "{}")
{
    public Dictionary<string, string> Headers { get; init; } = new();
    public TimeSpan Delay { get; init; }
    public bool NetworkError { get; init; }
}

/// <summary>Records every request and answers with <c>respond</c> (200 and "{}" by default).</summary>
public sealed class FakeServer(Func<CapturedRequest, FakeResponse>? respond = null) : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var captured = new CapturedRequest(
            request.Method.Method, request.RequestUri!.AbsoluteUri, headers, request.Content?.Headers.ContentType?.ToString(), body);
        Requests.Add(captured);

        var answer = respond?.Invoke(captured) ?? new FakeResponse();
        if (answer.NetworkError)
            throw new HttpRequestException("Connection refused.");
        if (answer.Delay > TimeSpan.Zero)
            await Task.Delay(answer.Delay, cancellationToken);

        var response = new HttpResponseMessage(answer.Status)
        {
            Content = new StringContent(answer.Body, Encoding.UTF8, "application/json")
        };
        foreach (var (name, value) in answer.Headers)
            response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }
}

/// <summary>Parses a workflow from YAML text and runs it against a <see cref="FakeServer"/>.</summary>
public static class Run
{
    public static async Task<(WorkflowReport Report, FakeServer Server)> Workflow(
        string yaml, Func<CapturedRequest, FakeResponse>? respond = null, Dictionary<string, string>? environment = null)
    {
        var workflow = WorkflowLoader.Parse(yaml, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "test.yaml"));
        var server = new FakeServer(respond);
        var engine = new WorkflowEngine(new HttpClient(server));
        var env = (environment ?? new()).ToDictionary(e => e.Key, e => JsonValues.FromString(e.Value));
        return (await engine.ExecuteAsync(workflow, env), server);
    }

    public static StepReport Single(this WorkflowReport report) => report.StepReports.Single();

    public static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
