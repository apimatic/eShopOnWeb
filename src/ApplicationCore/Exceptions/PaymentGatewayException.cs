using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the payment provider (PayPal) rejects a request or is unreachable. Carries a
/// caller-safe message and the HTTP status the boundary should surface — never the provider's
/// raw exception detail. A provider rejection the caller can act on maps to a 4xx; an outage or
/// unknown failure maps to 5xx.
/// </summary>
public class PaymentGatewayException : Exception
{
    /// <summary>The HTTP status this failure should surface as at the API boundary.</summary>
    public int HttpStatusCode { get; }

    /// <summary>PayPal's debug id, when available, to aid support/reconciliation. Not shown to shoppers.</summary>
    public string? DebugId { get; }

    public PaymentGatewayException(string message, int httpStatusCode, string? debugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
        DebugId = debugId;
    }
}
