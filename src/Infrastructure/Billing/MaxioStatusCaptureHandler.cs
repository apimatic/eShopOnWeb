using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Captures the last HTTP status so a JsonException on the error path can be treated as a rejection.
/// </summary>
public sealed class MaxioStatusCaptureHandler : DelegatingHandler
{
    private static readonly AsyncLocal<HttpStatusCode?> Last = new();

    public static HttpStatusCode? LastStatusCode => Last.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        Last.Value = response.StatusCode;
        return response;
    }
}
