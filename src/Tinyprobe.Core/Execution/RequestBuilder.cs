using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tinyprobe.Core.Definitions;

namespace Tinyprobe.Core.Execution;

/// <summary>
/// Where the files of multipart bodies are read: relative paths from <see cref="BaseDirectory"/> (the directory of
/// the file tinyprobe was given); with a <see cref="Root"/>, only files inside it (the MCP server's workspace).
/// </summary>
internal sealed record FileAccess(string BaseDirectory, string? Root = null);

/// <summary>Builds the HTTP request of an endpoint call from its templates and the call's variables.</summary>
internal static class RequestBuilder
{
    /// <summary>
    /// The body as recorded (<c>-v</c>, ndjson), when it is not the body as sent: a multipart body with each file's
    /// content replaced by one line, so recorded requests stay readable and small.
    /// </summary>
    public static readonly HttpRequestOptionsKey<string> RecordedBody = new("tinyprobe.recorded_body");

    public static HttpRequestMessage Build(EndpointDefinition endpoint, CallScope scope, FileAccess files)
    {
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), BuildUri(endpoint, scope));

        if (endpoint.Body is not null)
        {
            var body = TemplateRenderer.RenderValue(endpoint.Body, scope);
            request.Content = body.ValueKind == JsonValueKind.String
                ? new StringContent(body.GetString()!, Encoding.UTF8, "text/plain")
                : new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        }
        else if (endpoint.Form is { } form)
        {
            request.Content = new FormUrlEncodedContent(form.SelectMany(field => Texts(field, scope).Select(text => KeyValuePair.Create(field.Name, text))));
        }
        else if (endpoint.Multipart is { } multipart)
        {
            var (content, recorded) = Multipart(multipart, scope, files);
            request.Content = content;
            request.Options.Set(RecordedBody, recorded);
        }
        var ownContentType = endpoint.Form is not null || endpoint.Multipart is not null;

        // File headers, then auth sets, then endpoint headers: a later header with the same name wins.
        var headers = new Dictionary<string, (string Name, string Value)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in endpoint.Headers)
            headers[name] = (name, TemplateRenderer.RenderText(value, scope));

        foreach (var (name, value) in headers.Values)
        {
            // A Content-Type of the file or of an auth set would break a form or multipart body.
            if (ownContentType && name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                continue;
            if (request.Headers.TryAddWithoutValidation(name, value))
                continue;

            // Content headers (Content-Type, Content-Language...) belong to the body
            // and are ignored when the request has none.
            if (request.Content is not null)
            {
                request.Content.Headers.Remove(name);
                request.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return request;
    }

    /// <summary>The text values of a field: a list (written, or the value of a placeholder) sends the field once per item.</summary>
    private static IEnumerable<string> Texts(BodyField field, CallScope scope)
    {
        var value = TemplateRenderer.RenderValue(field.Value!, scope);
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Select(JsonValues.ToText).ToList();
        if (value.ValueKind == JsonValueKind.Object)
            throw new InvalidOperationException($"Field '{field.Name}' is an object: a form or multipart field is text.");
        return [JsonValues.ToText(value)];
    }

    private static (MultipartFormDataContent Content, string Recorded) Multipart(
        IReadOnlyList<BodyField> fields, CallScope scope, FileAccess files)
    {
        var boundary = $"tinyprobe-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}";
        var content = new MultipartFormDataContent(boundary);
        var recorded = new StringBuilder();

        foreach (var field in fields)
        {
            if (field.File is null)
            {
                foreach (var text in Texts(field, scope))
                {
                    // No Content-Type on text fields, as browsers send them.
                    var part = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
                    part.Headers.ContentDisposition = Disposition(field.Name, fileName: null);
                    content.Add(part);
                    Record(recorded, boundary, part, text);
                }
                continue;
            }

            var path = TemplateRenderer.RenderText(field.File, scope);
            var bytes = ReadFile(field.Name, path, files);
            var fileName = field.FileName is null ? Path.GetFileName(path) : TemplateRenderer.RenderText(field.FileName, scope);
            var contentType = field.ContentType is null ? ContentTypeOf(path) : TemplateRenderer.RenderText(field.ContentType, scope);

            var filePart = new ByteArrayContent(bytes);
            filePart.Headers.ContentDisposition = Disposition(field.Name, fileName);
            filePart.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            content.Add(filePart);
            Record(recorded, boundary, filePart, $"(file {path}, {FormatSize(bytes.Length)})");
        }

        recorded.Append($"--{boundary}--");
        return (content, recorded.ToString());
    }

    private static ContentDispositionHeaderValue Disposition(string name, string? fileName)
    {
        // Set by hand: MultipartFormDataContent.Add(content, name, fileName) also writes filename*=utf-8'',
        // which some servers reject.
        var disposition = new ContentDispositionHeaderValue("form-data") { Name = Quote(name) };
        if (fileName is not null)
            disposition.FileName = Quote(fileName);
        return disposition;
    }

    private static string Quote(string text) => $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    private static void Record(StringBuilder recorded, string boundary, HttpContent part, string body)
    {
        recorded.Append($"--{boundary}\n");
        foreach (var header in part.Headers)
            recorded.Append($"{header.Key}: {string.Join(", ", header.Value)}\n");
        recorded.Append('\n').Append(body).Append('\n');
    }

    private static byte[] ReadFile(string field, string path, FileAccess files)
    {
        var fullPath = Path.GetFullPath(Path.Combine(files.BaseDirectory, path));
        if (files.Root is { } root && !IsInside(fullPath, Path.GetFullPath(root)))
            throw new InvalidOperationException($"Cannot read the file for multipart field '{field}': {path} is outside the working directory of the MCP server");
        try
        {
            return File.ReadAllBytes(fullPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidOperationException($"Cannot read the file for multipart field '{field}': {path} was not found");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Cannot read the file for multipart field '{field}': {ex.Message}");
        }
    }

    private static bool IsInside(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".pdf"] = "application/pdf",
        [".json"] = "application/json",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".zip"] = "application/zip"
    };

    internal static string ContentTypeOf(string path) =>
        ContentTypes.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.#} MB")
    };

    private static Uri BuildUri(EndpointDefinition endpoint, CallScope scope)
    {
        var path = TemplateRenderer.RenderText(endpoint.Path, scope);

        var query = new StringBuilder();
        foreach (var name in endpoint.Query)
        {
            if (!scope.TryGetArgument(name, out var value))
                continue;
            query.Append(query.Length == 0 ? (path.Contains('?') ? '&' : '?') : '&')
                 .Append(Uri.EscapeDataString(name)).Append('=')
                 .Append(Uri.EscapeDataString(JsonValues.ToText(value)));
        }
        path += query;

        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute;
        }

        var baseUrl = endpoint.BaseUrl is null ? string.Empty : TemplateRenderer.RenderText(endpoint.BaseUrl, scope);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            // Relative to HttpClient.BaseAddress, if any.
            return new Uri(path, UriKind.RelativeOrAbsolute);
        }

        // Plain concatenation so a base path (e.g. https://host/v1/) is preserved
        // regardless of leading/trailing slashes.
        return new Uri($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
    }
}
