using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Rewrites the scheme/host/port (and optional base path) of every outgoing request to a
/// configured base address, preserving the SDK-generated path and query. This lets us honor the
/// optional <c>Maxio:BaseUrl</c> verbatim override while still using the official Maxio SDK,
/// whose builder otherwise only derives the address from the site subdomain + environment.
/// </summary>
public class BaseAddressRewriteHandler : DelegatingHandler
{
    private readonly Uri _baseAddress;
    private readonly string _basePath;

    public BaseAddressRewriteHandler(string baseUrl)
    {
        _baseAddress = new Uri(baseUrl, UriKind.Absolute);
        // Any path component of the override is treated as a prefix for every request path.
        _basePath = _baseAddress.AbsolutePath.TrimEnd('/');
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not null)
        {
            var builder = new UriBuilder(_baseAddress)
            {
                Path = _basePath + request.RequestUri.AbsolutePath,
                Query = request.RequestUri.Query.TrimStart('?')
            };
            request.RequestUri = builder.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
