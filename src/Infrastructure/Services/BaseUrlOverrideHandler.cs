using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services;

internal sealed class BaseUrlOverrideHandler : DelegatingHandler
{
    private readonly Uri _baseUri;

    public BaseUrlOverrideHandler(string baseUrl)
    {
        _baseUri = new Uri(baseUrl.TrimEnd('/'));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri != null)
        {
            var builder = new UriBuilder(request.RequestUri)
            {
                Scheme = _baseUri.Scheme,
                Host = _baseUri.Host,
                Port = _baseUri.Port
            };
            request.RequestUri = builder.Uri;
        }
        return base.SendAsync(request, cancellationToken);
    }
}
