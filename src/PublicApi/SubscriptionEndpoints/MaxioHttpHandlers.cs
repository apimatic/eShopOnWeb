using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioWriteGuardHandler(MaxioCallContext callContext) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post)
        {
            callContext.RegisterWrite();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class MaxioResponseStatusHandler(MaxioCallContext callContext) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        callContext.LastStatusCode = response.StatusCode;
        return response;
    }
}
