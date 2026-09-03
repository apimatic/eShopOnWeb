using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Remembers the last HTTP status for this async flow so a JsonException thrown while
/// binding an error body can still be mapped as a provider rejection rather than an outage.
/// </summary>
internal sealed class LastStatusCaptureHandler : DelegatingHandler
{
    private static readonly AsyncLocal<HttpStatusCode?> Last = new();

    public static HttpStatusCode? LastStatus => Last.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            Last.Value = response.StatusCode;
            return response;
        }
        catch
        {
            Last.Value = null;
            throw;
        }
    }
}
