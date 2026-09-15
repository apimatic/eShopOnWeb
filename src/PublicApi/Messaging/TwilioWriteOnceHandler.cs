using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

public sealed class TwilioWriteOnceHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = TwilioWriteOnceScope.Current;
        if (scope is not null && request.Method == HttpMethod.Post)
        {
            if (Interlocked.Increment(ref scope.AttemptedPosts) > 1)
            {
                throw new TwilioDuplicateWriteException();
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
