using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Captures the last HTTP status seen on the PayPal pipeline so a
/// <see cref="System.Text.Json.JsonException"/> can be mapped as a rejection (4xx)
/// versus an unreadable success (2xx).
/// </summary>
public sealed class PayPalStatusCaptureHandler : DelegatingHandler
{
    private static readonly AsyncLocal<HttpStatusCode?> LastStatusLocal = new();

    public static HttpStatusCode? LastStatus => LastStatusLocal.Value;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        LastStatusLocal.Value = response.StatusCode;
        return response;
    }
}
