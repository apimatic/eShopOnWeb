using System.Net;

namespace Microsoft.eShopWeb.PaymentIntegrationTests;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that answers by inspecting the request path — the SDK's
/// constructor <c>HttpClient</c> is the test seam, so no real network calls happen.
/// </summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, (HttpStatusCode Status, string Json)> _responder;
    public List<HttpRequestMessage> Requests { get; } = new();

    public StubHandler(Func<HttpRequestMessage, (HttpStatusCode, string)> responder)
        => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        var (status, json) = _responder(request);
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
        return Task.FromResult(response);
    }

    /// <summary>The OAuth token every call needs first.</summary>
    public static (HttpStatusCode, string) TokenResponse()
        => (HttpStatusCode.OK, """{"access_token":"test-token","token_type":"Bearer","expires_in":32400}""");
}
