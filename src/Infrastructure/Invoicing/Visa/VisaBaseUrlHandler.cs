using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing.Visa;

/// <summary>
/// Forces every outgoing request the CyberSource SDK makes to be sent to the configured
/// <c>Visa:BaseUrl</c>, verbatim. The SDK composes request URIs from its own host setting; this
/// handler rewrites the scheme, host, port and any base path of each request so that no provider
/// call can bypass <c>Visa:BaseUrl</c> or carry a hard-coded host. When <c>Visa:BaseUrl</c> is not
/// set the request is left untouched and the SDK's own address is used.
/// </summary>
public class VisaBaseUrlHandler : DelegatingHandler
{
    private readonly IOptions<VisaSettings> _settings;

    public VisaBaseUrlHandler(IOptions<VisaSettings> settings)
    {
        _settings = settings;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var baseUrl = _settings.Value.BaseUrl;
        if (!string.IsNullOrWhiteSpace(baseUrl)
            && request.RequestUri is not null
            && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            request.RequestUri = RebaseOnto(baseUri, request.RequestUri);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static Uri RebaseOnto(Uri baseUri, Uri requestUri)
    {
        // Keep the SDK's path and query, but take the address (scheme/host/port) and any path
        // prefix from the configured base URL exactly as given.
        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var builder = new UriBuilder(requestUri)
        {
            Scheme = baseUri.Scheme,
            Host = baseUri.Host,
            Port = baseUri.Port,
            Path = string.Concat(basePath, requestUri.AbsolutePath)
        };

        return builder.Uri;
    }
}
