using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Captures the last HTTP status for the current async flow so a JsonException
/// raised while parsing an error body can still be mapped as a client vs server failure.
/// </summary>
internal sealed class LastStatusHandler : DelegatingHandler
{
    private static readonly AsyncLocal<int?> Status = new();

    public static int? LastStatusCode => Status.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        Status.Value = (int)response.StatusCode;
        return response;
    }
}
