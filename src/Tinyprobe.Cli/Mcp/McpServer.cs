using System.Net;
using System.Text.Json;
using System.Text;

namespace Tinyprobe.Cli.Mcp;

/// <summary>
/// <c>tinyprobe mcp</c>: an MCP server (JSON-RPC 2.0, one message per line) on standard input and output.
/// Written by hand with <see cref="JsonDocument"/> and <see cref="Utf8JsonWriter"/>, like ProtocolJson,
/// so it stays Native AOT-safe. See docs/specs/0005-mcp-server.md.
/// </summary>
internal sealed class McpServer(string version, Func<CookieContainer, HttpMessageHandler> createHandler)
{
    /// <summary>The MCP versions this server speaks, newest first.</summary>
    public static readonly string[] ProtocolVersions = ["2025-06-18", "2025-03-26", "2024-11-05"];

    private const string Instructions =
        "tinyprobe tests HTTP APIs with YAML files: API files declare endpoints, workflows call them and check the responses. " +
        "Call `guide` before writing or changing tinyprobe files. Check a file with `inspect`, look at a real response with `call`, " +
        "and run workflows or folders with `run`.";

    private readonly McpTools _tools = new(version, createHandler);

    public async Task RunAsync(TextReader input, TextWriter output)
    {
        // One request at a time: tool calls are short, and the answers keep the order of the requests.
        while (await input.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (await HandleAsync(line) is { } response)
            {
                await output.WriteAsync(response + "\n");
                await output.FlushAsync();
            }
        }
    }

    /// <summary>The answer to one message, or null for notifications and for responses sent by the client.</summary>
    internal async Task<string?> HandleAsync(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return Error(null, -32700, "Parse error: the message is not valid JSON.");
        }

        using (document)
        {
            var message = document.RootElement;
            if (message.ValueKind != JsonValueKind.Object)
                return Error(null, -32600, "Invalid request: a message is a JSON-RPC object.");

            JsonElement? id = message.TryGetProperty("id", out var idElement) ? idElement.Clone() : null;
            if (!message.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
            {
                // A response to a request of ours (we send none), or garbage without an id: nothing to answer.
                var isResponse = message.TryGetProperty("result", out _) || message.TryGetProperty("error", out _);
                return id is null || isResponse ? null : Error(id, -32600, "Invalid request: no method.");
            }

            // Notifications (initialized, cancelled) need no answer, and the server has no state to change.
            if (id is null)
                return null;

            var parameters = message.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
            var method = methodElement.GetString();
            switch (method)
            {
                case "initialize":
                    return Result(id, w => Initialize(w, parameters));
                case "ping":
                    return Result(id, _ => { });
                case "tools/list":
                    return Result(id, McpTools.WriteList);
                case "tools/call":
                    return await CallToolAsync(id.Value, parameters);
                default:
                    return Error(id, -32601, $"Method not found: {method}");
            }
        }
    }

    private void Initialize(Utf8JsonWriter w, JsonElement parameters)
    {
        var requested = parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("protocolVersion", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

        w.WriteString("protocolVersion", requested is not null && ProtocolVersions.Contains(requested) ? requested : ProtocolVersions[0]);
        w.WriteStartObject("capabilities");
        w.WriteStartObject("tools");
        w.WriteBoolean("listChanged", false);
        w.WriteEndObject();
        w.WriteEndObject();
        w.WriteStartObject("serverInfo");
        w.WriteString("name", "tinyprobe");
        w.WriteString("title", "tinyprobe");
        w.WriteString("version", version);
        w.WriteEndObject();
        w.WriteString("instructions", Instructions);
    }

    private async Task<string> CallToolAsync(JsonElement id, JsonElement parameters)
    {
        var name = parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()!
            : null;
        if (name is null || !McpTools.Names.Contains(name))
            return Error(id, -32602, name is null ? "Invalid params: no tool name." : $"Unknown tool: {name}");

        var arguments = parameters.TryGetProperty("arguments", out var a) ? a : default;
        var (text, isError) = await _tools.CallAsync(name, arguments);

        return Result(id, w =>
        {
            w.WriteStartArray("content");
            w.WriteStartObject();
            w.WriteString("type", "text");
            w.WriteString("text", text);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteBoolean("isError", isError);
        });
    }

    private static string Result(JsonElement? id, Action<Utf8JsonWriter> result) => Message(id, w =>
    {
        w.WriteStartObject("result");
        result(w);
        w.WriteEndObject();
    });

    private static string Error(JsonElement? id, int code, string text) => Message(id, w =>
    {
        w.WriteStartObject("error");
        w.WriteNumber("code", code);
        w.WriteString("message", text);
        w.WriteEndObject();
    });

    private static string Message(JsonElement? id, Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WritePropertyName("id");
            if (id is { } value)
                value.WriteTo(w);
            else
                w.WriteNullValue();
            body(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
