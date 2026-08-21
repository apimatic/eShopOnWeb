using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Carries the HTTP status of the last PayPal response back to the calling service, out of band.
///
/// The SDK surfaces a successful call as only the deserialized body, and a drifted error/2xx body
/// as a <see cref="System.Text.Json.JsonException"/> that destroys the HTTP status with it. Capturing
/// the status in a <see cref="DelegatingHandler"/> lets the error boundary map a provider 4xx to a
/// client 4xx and everything else to 5xx, regardless of which exception shape reaches it.
///
/// The parent creates a <see cref="StatusBox"/> before the call; the handler (running in a child async
/// flow) mutates that same instance, so the parent sees the write even though AsyncLocal only flows
/// values downward.
/// </summary>
public static class PayPalResponseContext
{
    private static readonly AsyncLocal<StatusBox?> _current = new();

    public static StatusBox Begin()
    {
        var box = new StatusBox();
        _current.Value = box;
        return box;
    }

    public static StatusBox? Current => _current.Value;

    public sealed class StatusBox
    {
        public int? StatusCode { get; set; }

        /// <summary>Raw error body of the last non-2xx response — logged server-side for diagnostics, never surfaced.</summary>
        public string? ErrorBody { get; set; }
    }
}
