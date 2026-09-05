using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioWriteRetryGuardHandler : DelegatingHandler
{
    private readonly MaxioWriteScope _writeScope;

    public MaxioWriteRetryGuardHandler(MaxioWriteScope writeScope)
    {
        _writeScope = writeScope;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && _writeScope.CurrentReference is not null && !_writeScope.TryMarkSent())
        {
            throw new MaxioWriteRetryBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
