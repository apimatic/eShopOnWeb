using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Services.MaxioBillingClientTests;

/// <summary>The HttpClient-seam test double for the Maxio SDK client (per the SDK's dotnet-testing guidance): no real network calls, and the outgoing request is captured for assertions.</summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public HttpRequestMessage? LastRequest { get; private set; }

    public StubHttpMessageHandler(System.Net.HttpStatusCode statusCode, string json)
        : this(_ => new HttpResponseMessage(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") })
    {
    }

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(_responder(request));
    }
}
