using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Enforces <see cref="MaxioWriteGuard"/> on the wire: a request refused here never reaches the network.
/// </summary>
public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Count the send BEFORE it goes out: a request that failed on the way out may still have been received.
        if (!MaxioWriteGuard.TryAuthorizeSend())
        {
            throw new MaxioDuplicateSendBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
