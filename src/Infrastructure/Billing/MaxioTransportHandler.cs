using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioTransportHandler(MaxioCallContext callContext) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        callContext.BeforeSend(request);
        var response = await base.SendAsync(request, cancellationToken);
        callContext.RecordResponse(response.StatusCode);
        return response;
    }
}
