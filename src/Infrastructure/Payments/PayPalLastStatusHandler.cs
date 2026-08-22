using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PayPalLastStatusHandler : DelegatingHandler
{
    private static readonly AsyncLocal<int?> LastStatus = new();

    public static int? Current => LastStatus.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        LastStatus.Value = (int)response.StatusCode;
        return response;
    }
}
