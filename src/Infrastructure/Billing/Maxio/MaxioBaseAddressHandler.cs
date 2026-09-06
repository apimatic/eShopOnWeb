using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Redirects the SDK's generated absolute URLs at the configured base address.
/// </summary>
/// <remarks>
/// The Advanced Billing SDK composes every URL from a fixed template (<c>https://{site}.chargify.com</c>,
/// or the EU equivalent) and offers no hook to replace it. Rewriting the authority here is the supported
/// extension point: the SDK is handed this <see cref="HttpClient"/> pipeline via
/// <c>HttpClientInstance</c>, so it never has to know the request went somewhere else. Paths, query
/// strings, headers and bodies are left untouched, so the wire contract is exactly what the SDK intended.
/// </remarks>
internal sealed class MaxioBaseAddressHandler : DelegatingHandler
{
    /// <summary>
    /// Events-Based Billing lives on a separate host that is not derived from the site subdomain, so an
    /// operator overriding the main API address is not asking for EBB traffic to move with it.
    /// </summary>
    private const string EventsHost = "events.chargify.com";

    private readonly Uri _baseAddress;

    public MaxioBaseAddressHandler(Uri baseAddress)
    {
        _baseAddress = baseAddress ?? throw new ArgumentNullException(nameof(baseAddress));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var original = request.RequestUri;

        if (original is not null &&
            !string.Equals(original.Host, EventsHost, StringComparison.OrdinalIgnoreCase))
        {
            request.RequestUri = Rebase(original, _baseAddress);
        }

        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Grafts the path and query of <paramref name="original"/> onto <paramref name="baseAddress"/>.
    /// </summary>
    internal static Uri Rebase(Uri original, Uri baseAddress)
    {
        // AbsolutePath keeps the escaping the SDK produced; trimming the leading slash makes it
        // relative so that any path prefix on the base address (a gateway route, say) is preserved.
        var relative = original.AbsolutePath.TrimStart('/') + original.Query;
        return new Uri(baseAddress, relative);
    }
}
