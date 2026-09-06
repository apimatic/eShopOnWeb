using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Honours <c>Maxio:BaseUrl</c>. The Maxio SDK always builds absolute URLs from the site subdomain, so
/// when an explicit base address is configured this handler rewrites the scheme, host, port and any path
/// prefix of the outgoing request to that address verbatim, leaving the resource path and query intact.
/// It is a no-op when no override is configured.
/// </summary>
public class MaxioBaseAddressHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<MaxioOptions> _options;

    public MaxioBaseAddressHandler(IOptionsMonitor<MaxioOptions> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var baseUrl = _options.CurrentValue.BaseUrl;

        if (request.RequestUri is not null &&
            !string.IsNullOrWhiteSpace(baseUrl) &&
            Uri.TryCreate(baseUrl, UriKind.Absolute, out var configuredBase))
        {
            request.RequestUri = Rebase(request.RequestUri, configuredBase);
        }

        return base.SendAsync(request, cancellationToken);
    }

    internal static Uri Rebase(Uri requestUri, Uri configuredBase)
    {
        var builder = new UriBuilder(requestUri)
        {
            Scheme = configuredBase.Scheme,
            Host = configuredBase.Host,
            Port = configuredBase.IsDefaultPort ? -1 : configuredBase.Port,
        };

        var prefix = configuredBase.AbsolutePath.TrimEnd('/');
        if (prefix.Length > 0)
        {
            builder.Path = prefix + builder.Path;
        }

        return builder.Uri;
    }
}
