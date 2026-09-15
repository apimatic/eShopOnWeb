using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Captures the last HTTP status so a <see cref="System.Text.Json.JsonException"/> thrown while
/// constructing a typed PayPal error can still be mapped to the provider's 4xx vs 5xx.
/// </summary>
public sealed class LastStatusHandler : DelegatingHandler
{
    public static readonly AsyncLocal<HttpStatusCode?> LastStatus = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        LastStatus.Value = response.StatusCode;
        return response;
    }
}
