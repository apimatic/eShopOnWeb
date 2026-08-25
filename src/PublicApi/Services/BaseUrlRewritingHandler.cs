using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

// Rewrites every outgoing request URI to use the configured base URL,
// preserving path and query. Used to override the PayPal SDK's built-in
// environment URLs (including token requests) when PayPal:BaseUrl is set.
public class BaseUrlRewritingHandler : DelegatingHandler
{
    private readonly Uri _base;

    public BaseUrlRewritingHandler(string baseUrl)
    {
        _base = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri != null)
        {
            var path = request.RequestUri.PathAndQuery.TrimStart('/');
            request.RequestUri = new Uri(_base, path);
        }
        return base.SendAsync(request, ct);
    }
}
