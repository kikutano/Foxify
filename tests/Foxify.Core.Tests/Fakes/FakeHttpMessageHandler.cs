namespace Foxify.Core.Tests.Fakes;

public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _responseToSend;

    public HttpRequestMessage? LastRequest { get; private set; }
    public int CallCount { get; private set; }

    public FakeHttpMessageHandler(HttpResponseMessage responseToSend)
    {
        _responseToSend = responseToSend;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        return Task.FromResult(_responseToSend);
    }
}
