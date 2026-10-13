using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>
/// When <c>Walmart:BaseUrl</c> is configured, rewrites the scheme, host and port of every outgoing Walmart
/// request — API calls and the OAuth token fetch alike, since both travel through the one HttpClient the SDK
/// is built over. Everything after the host (the contract's base path, route, and query) is preserved exactly.
/// With no override configured, requests pass through untouched (to the SDK's own production base URL).
/// </summary>
public sealed class WalmartBaseUrlHandler : DelegatingHandler
{
    private readonly Uri? _override;

    public WalmartBaseUrlHandler(IOptions<WalmartSettings> settings)
    {
        var baseUrl = settings.Value.BaseUrl;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed))
            {
                throw new InvalidOperationException(
                    $"Walmart:BaseUrl is not a valid absolute URL ('scheme://host[:port]' expected).");
            }
            _override = parsed;
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_override is not null && request.RequestUri is not null)
        {
            // Replace only scheme/host/port; keep PathAndQuery exactly as the SDK formed it.
            var builder = new UriBuilder(request.RequestUri)
            {
                Scheme = _override.Scheme,
                Host = _override.Host,
                Port = _override.Port,
            };
            request.RequestUri = builder.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
