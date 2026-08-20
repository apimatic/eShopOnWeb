using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal sealed class TwilioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method) && !TwilioWriteOnceScope.TryMarkSent())
        {
            throw new TwilioDuplicateWriteException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method)
        => method == HttpMethod.Post
           || method == HttpMethod.Put
           || method == HttpMethod.Patch
           || method == HttpMethod.Delete;
}
