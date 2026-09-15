using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal sealed class LastHttpStatusHandler : DelegatingHandler
{
    private static readonly AsyncLocal<HttpStatusCode?> StatusSlot = new();

    public static HttpStatusCode? LastStatus => StatusSlot.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        StatusSlot.Value = response.StatusCode;
        return response;
    }
}
