using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at the payment-gateway boundary when PayPal reports an error or is unreachable. Carries a
/// caller-safe message plus the transport status and PayPal's correlation (<c>debug_id</c>) for the
/// logs — never the raw provider payload or any card data.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, HttpStatusCode? statusCode = null, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }

    /// <summary>The HTTP status PayPal returned, when one was available.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>PayPal's <c>debug_id</c> correlation value, when present, for support/log correlation.</summary>
    public string? DebugId { get; }
}
