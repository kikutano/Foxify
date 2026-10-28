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
    /// <summary><c>Set-Cookie</c> headers, one per cookie.</summary>
    public string[] SetCookies { get; init; } = [];
    public TimeSpan Delay { get; init; }
    public bool NetworkError { get; init; }
}

/// <summary>
/// Records every request and answers with <c>respond</c> (200 and "{}" by default).
/// With <see cref="Cookies"/>, it keeps and sends cookies as <see cref="HttpClientHandler"/> does (no redirects).
/// </summary>
public sealed class FakeServer(Func<CapturedRequest, FakeResponse>? respond = null) : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = new();

    public CookieContainer? Cookies { get; init; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        if (Cookies?.GetCookieHeader(request.RequestUri!) is { Length: > 0 } fromJar)
            headers["Cookie"] = headers.TryGetValue("Cookie", out var written) ? $"{written}; {fromJar}" : fromJar;
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
        foreach (var cookie in answer.SetCookies)
        {
            response.Headers.TryAddWithoutValidation("Set-Cookie", cookie);
            Cookies?.SetCookies(request.RequestUri!, cookie);
        }
        response.RequestMessage = request;
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
        var cookies = new CookieContainer();
        var server = new FakeServer(respond) { Cookies = cookies };
        var engine = new WorkflowEngine(new HttpClient(server), cookies: cookies);
        var env = (environment ?? new()).ToDictionary(e => e.Key, e => JsonValues.FromString(e.Value));
        return (await engine.ExecuteAsync(workflow, env), server);
    }

    public static StepReport Single(this WorkflowReport report) => report.StepReports.Single();

    public static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
