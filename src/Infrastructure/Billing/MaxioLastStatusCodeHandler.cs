using System.Net.Http;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Remembers the most recent response status that passed through any instance of this handler.
/// The SDK surfaces only deserialized bodies; when a non-2xx body fails to match a generated
/// error model, the SDK throws System.Text.Json.JsonException and the HTTP status is lost with
/// the original exception — this handler preserves a best-effort status so the error boundary
/// can keep provider rejections distinct from outages. Instances are created by
/// IHttpClientFactory per handler pipeline; they share one static slot by design.
/// </summary>
public sealed class MaxioLastStatusCodeHandler : DelegatingHandler
{
    private static volatile int _lastStatus;

    public static HttpStatusCode LastStatus => (HttpStatusCode)_lastStatus;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        _lastStatus = (int)response.StatusCode;
        return response;
    }
}