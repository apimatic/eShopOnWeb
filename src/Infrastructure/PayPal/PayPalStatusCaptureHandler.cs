using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public sealed class PayPalStatusCaptureHandler : DelegatingHandler
{
    public static readonly AsyncLocal<HttpStatusCode?> LastStatus = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        LastStatus.Value = response.StatusCode;
        return response;
    }
}
