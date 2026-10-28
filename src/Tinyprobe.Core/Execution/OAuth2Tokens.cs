using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tinyprobe.Core.Definitions;

namespace Tinyprobe.Core.Execution;

/// <summary>
/// Tokens of <c>oauth2</c> auth sets (client credentials) for one run: requested on first use, then reused by every
/// endpoint with the same token URL, client and scope until <c>expires_in</c>, minus a margin.
/// </summary>
internal sealed class OAuth2Tokens(HttpClient httpClient, TextWriter log, TimeProvider time)
{
    /// <summary>A token is renewed this long before it expires, so a request does not reach the API with an expired one.</summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(250);
    private const int MaxShownBody = 500;

    private readonly Dictionary<string, (string Token, DateTimeOffset? Expires)> _tokens = new();

    public async Task<string> GetAsync(OAuth2ClientCredentials oauth2, CallScope scope, int maxRetries)
    {
        var url = TemplateRenderer.RenderText(oauth2.TokenUrl, scope);
        var clientId = TemplateRenderer.RenderText(oauth2.ClientId, scope);
        var clientSecret = TemplateRenderer.RenderText(oauth2.ClientSecret, scope);
        var fields = new List<KeyValuePair<string, string>> { new("grant_type", "client_credentials") };
        if (oauth2.Scope is { } scopeTemplate)
            fields.Add(new("scope", Scope(scopeTemplate, scope)));
        if (oauth2.ClientAuth == ClientAuthentication.Form)
        {
            fields.Add(new("client_id", clientId));
            fields.Add(new("client_secret", clientSecret));
        }
        fields.AddRange(oauth2.Form.Select(f => KeyValuePair.Create(f.Key, TemplateRenderer.RenderText(f.Value, scope))));

        var key = string.Join('\n', new[] { url, clientId, clientSecret, oauth2.ClientAuth.ToString() }.Concat(fields.Select(f => $"{f.Key}={f.Value}")));
        if (_tokens.TryGetValue(key, out var cached) && (cached.Expires is null || time.GetUtcNow() < cached.Expires))
        {
            log.WriteLine($"Using the OAuth2 token from {url}");
            return cached.Token;
        }

        log.WriteLine($"Requesting an OAuth2 token from {url}");
        var (token, expiresIn) = await RequestAsync(url, fields, oauth2.ClientAuth == ClientAuthentication.Basic ? (clientId, clientSecret) : null, maxRetries);
        _tokens[key] = (token, expiresIn is { } seconds ? time.GetUtcNow() + TimeSpan.FromSeconds(seconds) - ExpiryMargin : null);
        return token;
    }

    /// <summary><c>scope</c>: a value, or a list (written, or the value of a placeholder) joined with spaces.</summary>
    private static string Scope(Template template, CallScope scope)
    {
        var value = TemplateRenderer.RenderValue(template, scope);
        return value.ValueKind == JsonValueKind.Array
            ? string.Join(' ', value.EnumerateArray().Select(JsonValues.ToText))
            : JsonValues.ToText(value);
    }

    private async Task<(string Token, double? ExpiresIn)> RequestAsync(
        string url, List<KeyValuePair<string, string>> fields, (string Id, string Secret)? basic, int maxRetries)
    {
        for (int attempt = 0; ; attempt++)
        {
            // A request message can be sent only once, so it is rebuilt on every attempt.
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (basic is var (id, secret))
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", RequestBuilder.Base64Credentials(id, secret));

            try
            {
                using var response = await httpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw Failure(url, $"status {(int)response.StatusCode}, {Shorten(body)}");
                return ParseToken(url, body);
            }
            catch (Exception ex) when (attempt < maxRetries && ex is HttpRequestException or TaskCanceledException)
            {
                var delay = RetryBaseDelay * (attempt + 1);
                log.WriteLine($"{ex.Message} Retrying in {delay.TotalMilliseconds} ms ({attempt + 1}/{maxRetries})");
                await Task.Delay(delay);
            }
            catch (HttpRequestException ex)
            {
                throw Failure(url, ex.Message);
            }
            catch (TaskCanceledException)
            {
                throw Failure(url, "the request timed out");
            }
        }
    }

    private static (string Token, double? ExpiresIn) ParseToken(string url, string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("access_token", out var token) || token.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(token.GetString()))
                throw Failure(url, "the response has no access_token");

            // Some servers send expires_in as a string ("3600").
            double? expiresIn = root.TryGetProperty("expires_in", out var expires) ? expires.ValueKind switch
            {
                JsonValueKind.Number => expires.GetDouble(),
                JsonValueKind.String when double.TryParse(expires.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var s) => s,
                _ => null
            } : null;
            return (token.GetString()!, expiresIn);
        }
        catch (JsonException)
        {
            throw Failure(url, $"the response is not JSON: {Shorten(body)}");
        }
    }

    private static InvalidOperationException Failure(string url, string reason) =>
        new($"Cannot get an OAuth2 token from {url}: {reason}");

    private static string Shorten(string body)
    {
        var text = body.ReplaceLineEndings(" ").Trim();
        return text.Length <= MaxShownBody ? text : $"{text[..MaxShownBody]}…";
    }
}
