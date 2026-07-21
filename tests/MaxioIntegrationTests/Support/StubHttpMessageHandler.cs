using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Support;

/// <summary>
/// The HttpClient-constructor seam is how APIMatic-generated SDKs (including maxio-sdk-clone) are
/// unit-tested: pass an HttpClient backed by this fake handler so no real network call happens.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _json;

    public HttpRequestMessage? LastRequest { get; private set; }

    public StubHttpMessageHandler(HttpStatusCode statusCode, string json)
    {
        _statusCode = statusCode;
        _json = json;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_json, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}
