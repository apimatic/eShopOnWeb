using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class OncePerWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post
            || request.Method == HttpMethod.Patch
            || request.Method == HttpMethod.Put
            || request.Method == HttpMethod.Delete)
        {
            OncePerWriteGate.CountOrThrow();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
