using System.Net;
using System.Text.Json;

namespace Tyfapi.Core.Tests.Fakes;

public record LoginRequest(string username, string password);

public class FakeHttpLoginMessageHandler : HttpMessageHandler
{
    public const string UserName = "test_user";
    public const string Password = "test_password";
    public const string Token = "fake_token_123";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post)
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        if (request.RequestUri?.AbsolutePath.EndsWith("/login") != true)
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        string requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        var loginData = JsonSerializer.Deserialize<LoginRequest>(requestBody);

        if (loginData is null || loginData.username != UserName || loginData.password != Password)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { token = Token }))
        };
    }
}
