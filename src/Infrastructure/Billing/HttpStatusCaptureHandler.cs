using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Records the last HTTP status so a <see cref="System.Text.Json.JsonException"/> thrown while
/// constructing a typed error can still be mapped as a 4xx rejection rather than a 5xx outage.
/// </summary>
internal sealed class HttpStatusCaptureHandler : DelegatingHandler
{
    private static readonly AsyncLocal<HttpStatusCode?> LastStatus = new();

    public static HttpStatusCode? Current => LastStatus.Value;

    public static void Clear() => LastStatus.Value = null;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        LastStatus.Value = response.StatusCode;
        return response;
    }
}
