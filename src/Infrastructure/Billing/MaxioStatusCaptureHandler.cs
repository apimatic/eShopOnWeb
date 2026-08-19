using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Captures the last HTTP status for the current async flow so a JsonException
/// thrown while constructing an SDK error can still be mapped as a 4xx rejection
/// versus a 5xx unknown outcome.
/// </summary>
public sealed class MaxioStatusCaptureHandler : DelegatingHandler
{
    private static readonly AsyncLocal<HttpStatusCode?> LastStatus = new();

    public static HttpStatusCode? LastStatusCode => LastStatus.Value;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        LastStatus.Value = response.StatusCode;
        return response;
    }
}
