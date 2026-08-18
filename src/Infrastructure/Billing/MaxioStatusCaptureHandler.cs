using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Remembers the last HTTP status for this async flow so a <c>JsonException</c>
/// raised while constructing an error object can still be mapped as a provider
/// rejection (4xx) rather than an unknown 5xx outage.
/// </summary>
public sealed class MaxioStatusCaptureHandler : DelegatingHandler
{
    public static readonly AsyncLocal<HttpStatusCode?> LastStatus = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        LastStatus.Value = response.StatusCode;
        return response;
    }
}
