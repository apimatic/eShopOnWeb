using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class OnceOnlySendHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (WriteOnceScope.IsArmed && request.Method == HttpMethod.Post)
        {
            if (!WriteOnceScope.TryMarkSent())
            {
                throw new DuplicateWritePreventedException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
