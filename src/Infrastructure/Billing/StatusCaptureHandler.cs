using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Captures the last HTTP status so a JsonException thrown while constructing an error
/// object can still be mapped as a provider rejection rather than an unknown 2xx body.
/// </summary>
internal static class MaxioLastResponse
{
    private static readonly AsyncLocal<HttpStatusCode?> Status = new();

    public static HttpStatusCode? Code
    {
        get => Status.Value;
        set => Status.Value = value;
    }
}

internal sealed class StatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        MaxioLastResponse.Code = response.StatusCode;
        return response;
    }
}
