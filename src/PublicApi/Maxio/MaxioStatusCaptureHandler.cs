using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioStatusCaptureHandler : DelegatingHandler
{
    private static readonly AsyncLocal<int?> LastStatus = new();

    public static int? LastStatusCode => LastStatus.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        LastStatus.Value = (int)response.StatusCode;
        return response;
    }
}
