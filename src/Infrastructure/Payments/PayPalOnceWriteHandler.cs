using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalOnceWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method))
        {
            if (PayPalWriteScope.AlreadySent)
            {
                throw new PayPalDuplicateSendException();
            }

            PayPalWriteScope.MarkSent();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch || method == HttpMethod.Delete;
}
