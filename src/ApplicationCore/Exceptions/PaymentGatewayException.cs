using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment provider (PayPal) rejects a request or is unreachable. The message is
/// always caller-safe — provider/SDK internals and card data never reach it. <see cref="StatusCode"/>
/// lets the API boundary distinguish a caller-actionable rejection (a provider 4xx, surfaced as the
/// same client 4xx) from an outage or unreadable response (surfaced as 502/504), so a caller is not
/// told to retry something that can never succeed.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, HttpStatusCode statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status the API boundary should surface to the caller.</summary>
    public HttpStatusCode StatusCode { get; }
}
