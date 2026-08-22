using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class PayPalHttpStatusHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        PayPalCallContext.LastStatusCode = (int)response.StatusCode;
        return response;
    }
}

internal static class PayPalCallContext
{
    private static readonly AsyncLocal<int?> Status = new();

    public static int? LastStatusCode
    {
        get => Status.Value;
        set => Status.Value = value;
    }
}
