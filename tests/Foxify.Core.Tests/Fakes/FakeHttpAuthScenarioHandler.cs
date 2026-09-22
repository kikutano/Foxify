using System.Net;
using System.Text.Json;

namespace Foxify.Core.Tests.Fakes;

public record LoginRequest(string username, string password);

public class FakeHttpAuthScenarioHandler : HttpMessageHandler
{
    public const string UserName = "test_user";
    public const string Password = "test_password";
    public const string Token = "fake_token_123";
    public const string Email = "user@test.com";
    public const string Name = "John Matrix";
    public const int UserId = 123;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath.EndsWith("/login") == true)
            return PerformLoginAsync(request, cancellationToken);
        if (request.RequestUri?.AbsolutePath.EndsWith("/me") == true)
            return PerformGetMeAsync(request, cancellationToken);
        if (request.RequestUri?.AbsolutePath.Contains("/user") == true)
            return PerformGetUserDetailsAsync(request, cancellationToken);

        throw new NotImplementedException(
            $"{request.RequestUri?.AbsolutePath} Endpoint is not implemented");
    }

    private async Task<HttpResponseMessage> PerformLoginAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post)
            return new HttpResponseMessage(HttpStatusCode.BadRequest);

        string requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        var loginData = JsonSerializer.Deserialize<LoginRequest>(requestBody);

        if (loginData is null || loginData.username != UserName || loginData.password != Password)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { token = Token }))
        };
    }

    private async Task<HttpResponseMessage> PerformGetMeAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get)
            return new HttpResponseMessage(HttpStatusCode.BadRequest);

        if (!request.Headers.TryGetValues("Authorization", out var authHeader) ||
            authHeader.FirstOrDefault() != $"Bearer {Token}")
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var userInfo = new
        {
            userid = UserId,
            username = UserName,
            email = Email
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(userInfo))
        };
    }

    private async Task<HttpResponseMessage> PerformGetUserDetailsAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get)
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        if (!request.Headers.TryGetValues("Authorization", out var authHeader) ||
            authHeader.FirstOrDefault() != $"Bearer {Token}")
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var userDetails = new
        {
            userid = UserId,
            username = UserName,
            email = Email,
            name = Name
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(userDetails))
        };
    }
}
