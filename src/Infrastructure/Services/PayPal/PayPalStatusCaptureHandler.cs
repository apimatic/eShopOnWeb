using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Records the HTTP status (and, on failures, the raw error body) of each PayPal response into the
/// current <see cref="PayPalResponseContext"/> box, so the error boundary can read the real status
/// and provider issue codes even when the SDK collapses a response into a deserialization exception.
///
/// Deliberately does NOT log request bodies — those carry card numbers, which must never be logged.
/// Only PayPal error responses (which never contain card data) are captured, for server-side diagnostics.
/// </summary>
public sealed class PayPalStatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var box = PayPalResponseContext.Current;
        if (box is not null)
        {
            box.StatusCode = (int)response.StatusCode;

            // Buffer the error body so the boundary can log the provider's issue codes without
            // consuming the stream the SDK still needs to deserialize.
            if (!response.IsSuccessStatusCode && response.Content is not null)
            {
                await response.Content.LoadIntoBufferAsync();
                box.ErrorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            }
        }
        return response;
    }
}
