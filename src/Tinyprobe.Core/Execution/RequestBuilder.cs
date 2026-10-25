using System.Text;
using System.Text.Json;
using Tinyprobe.Core.Definitions;

namespace Tinyprobe.Core.Execution;

/// <summary>Builds the HTTP request of an endpoint call from its templates and the call's variables.</summary>
internal static class RequestBuilder
{
    public static HttpRequestMessage Build(EndpointDefinition endpoint, CallScope scope)
    {
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), BuildUri(endpoint, scope));

        if (endpoint.Body is not null)
        {
            var body = TemplateRenderer.RenderValue(endpoint.Body, scope);
            request.Content = body.ValueKind == JsonValueKind.String
                ? new StringContent(body.GetString()!, Encoding.UTF8, "text/plain")
                : new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        }

        // File headers, then auth sets, then endpoint headers: a later header with the same name wins.
        var headers = new Dictionary<string, (string Name, string Value)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in endpoint.Headers)
            headers[name] = (name, TemplateRenderer.RenderText(value, scope));

        foreach (var (name, value) in headers.Values)
        {
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
