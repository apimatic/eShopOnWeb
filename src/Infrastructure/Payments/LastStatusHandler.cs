using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class LastStatusHandler : DelegatingHandler
{
    public static readonly AsyncLocal<HttpResponseMessage?> LastResponse = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        LastResponse.Value = response;
        return response;
    }
}
