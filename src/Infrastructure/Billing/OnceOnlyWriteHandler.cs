using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class OnceOnlyWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method))
        {
            var guard = OnceOnlyWriteScope.Current;
            if (guard is not null)
            {
                if (guard.Sent)
                {
                    throw new DuplicateProviderWriteException();
                }

                guard.Sent = true;
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post ||
        method == HttpMethod.Put ||
        method == HttpMethod.Patch ||
        method == HttpMethod.Delete;
}
