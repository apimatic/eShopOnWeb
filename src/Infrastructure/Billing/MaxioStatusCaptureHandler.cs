using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioLastStatus
{
    private static readonly AsyncLocal<HttpStatusCode?> Status = new();

    public static HttpStatusCode? Current => Status.Value;

    public static void Set(HttpStatusCode status) => Status.Value = status;

    public static void Clear() => Status.Value = null;
}

internal sealed class MaxioStatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        MaxioLastStatus.Set(response.StatusCode);
        return response;
    }
}
