using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.TrendsEndpoints;

internal sealed class WikimediaUserAgentHandler : DelegatingHandler
{
    private const string UserAgent = "eShopOnWeb-trends/1.0 (shop-ops@example.com)";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Remove("User-Agent");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        return base.SendAsync(request, ct);
    }
}
