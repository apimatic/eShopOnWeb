using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public sealed class MaxioResponseStatusHandler(IMaxioResponseContext responseContext) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        responseContext.Record(response.StatusCode);
        return response;
    }
}

public sealed class MaxioWriteOnceHandler(IMaxioWriteGuard writeGuard) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !writeGuard.TryMarkPost())
        {
            throw new MaxioWriteReplayBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
