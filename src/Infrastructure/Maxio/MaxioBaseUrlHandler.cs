using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Rewrites the base address (scheme, host, port and any base path) of every outgoing request to the
/// configured <c>Maxio:BaseUrl</c>, while preserving the request path and query built by the SDK.
/// This lets the optional base-url override be honoured verbatim even though the SDK derives its address
/// from the site subdomain.
/// </summary>
internal sealed class MaxioBaseUrlHandler : DelegatingHandler
{
    private readonly string _scheme;
    private readonly string _host;
    private readonly int _port;
    private readonly string _basePath;

    public MaxioBaseUrlHandler(Uri baseUrl)
    {
        _scheme = baseUrl.Scheme;
        _host = baseUrl.Host;
        _port = baseUrl.Port;
        _basePath = baseUrl.AbsolutePath.TrimEnd('/');
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var original = request.RequestUri;
        if (original is not null)
        {
            var builder = new UriBuilder(original)
            {
                Scheme = _scheme,
                Host = _host,
                Port = _port,
                Path = _basePath + original.AbsolutePath,
            };
            request.RequestUri = builder.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
