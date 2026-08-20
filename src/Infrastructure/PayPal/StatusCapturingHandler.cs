using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Records the HTTP status of each PayPal response into <see cref="PayPalResponseContext"/> so the gateway
/// can read the real status even when the SDK converts the error into a typed model or a deserialization
/// failure. On a non-success response it also buffers the raw error body (so PayPal's own issue detail is
/// available for the operator-facing message), then restores the content so the SDK can still parse it.
/// Sits on the SDK's HttpClient pipeline.
/// </summary>
public sealed class StatusCapturingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        PayPalResponseContext.RecordStatus((int)response.StatusCode);

        if (!response.IsSuccessStatusCode && response.Content is not null)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            PayPalResponseContext.RecordErrorBody(body);

            var buffered = new StringContent(body);
            var contentType = response.Content.Headers.ContentType;
            if (contentType is not null)
            {
                buffered.Headers.ContentType = contentType;
            }
            response.Content = buffered;
        }

        return response;
    }
}
