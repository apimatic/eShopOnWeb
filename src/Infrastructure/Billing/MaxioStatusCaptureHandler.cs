using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioLastHttp
{
    private static readonly AsyncLocal<HttpStatusCode?> Status = new();

    public static HttpStatusCode? Last => Status.Value;

    public static void Set(HttpStatusCode statusCode) => Status.Value = statusCode;

    public static void Clear() => Status.Value = null;
}

internal sealed class MaxioStatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        MaxioLastHttp.Set(response.StatusCode);
        return response;
    }
}
