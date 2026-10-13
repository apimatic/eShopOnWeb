using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Walmart;

/// <summary>Records every request and returns a caller-supplied response per call.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> RequestBodies { get; } = new();

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        return _responder(request);
    }
}

/// <summary>An <see cref="IHttpClientFactory"/> that always hands out a client over one handler.</summary>
internal sealed class SingleClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

internal sealed class FixedTokenProvider : IWalmartAccessTokenProvider
{
    private readonly string _token;
    public FixedTokenProvider(string token = "test-access-token") => _token = token;
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(_token);
}
