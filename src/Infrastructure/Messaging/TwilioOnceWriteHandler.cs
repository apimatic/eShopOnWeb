using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioOnceWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isWrite = request.Method == HttpMethod.Post
            || request.Method == HttpMethod.Put
            || request.Method == HttpMethod.Patch
            || request.Method == HttpMethod.Delete;

        if (isWrite && !TwilioWriteScope.TryConsumeWrite())
        {
            throw new DuplicateWriteRefusedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
