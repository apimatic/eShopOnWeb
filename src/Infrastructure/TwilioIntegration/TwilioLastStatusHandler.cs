using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.TwilioIntegration;

internal sealed class TwilioLastStatusHandler : DelegatingHandler
{
    private static readonly AsyncLocal<HttpStatusCode?> Last = new();

    public static HttpStatusCode? LastStatus => Last.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        Last.Value = response.StatusCode;
        return response;
    }
}
