using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Records the HTTP status of the most recent Maxio response in the caller's async
/// context. The SDK discards the status when an error body fails to deserialize (the
/// JsonException replaces the SdkException), so the boundary reads it back here to tell
/// "provider rejected the request" (4xx) apart from "response unreadable" (5xx).
/// Across a retry sequence the last attempt's status wins, which is the one that
/// produced the failure being translated.
/// </summary>
public sealed class MaxioStatusCaptureHandler : DelegatingHandler
{
    private static readonly AsyncLocal<HttpStatusCode?> _lastStatus = new();

    public static HttpStatusCode? LastStatus => _lastStatus.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        _lastStatus.Value = response.StatusCode;
        return response;
    }
}
