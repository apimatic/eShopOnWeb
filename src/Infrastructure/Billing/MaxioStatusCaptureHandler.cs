using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioCallStatus
{
    private static readonly AsyncLocal<int?> LastStatus = new();

    public static int? LastStatusCode
    {
        get => LastStatus.Value;
        set => LastStatus.Value = value;
    }
}

internal sealed class MaxioStatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        MaxioCallStatus.LastStatusCode = (int)response.StatusCode;
        return response;
    }
}
