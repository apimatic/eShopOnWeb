using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the payment processor (PayPal) rejects or fails an operation — a declined card, a
/// failed capture, an API error. Carries a human-readable message and, when available, PayPal's
/// debug id. Maps to HTTP 502 Bad Gateway at the API boundary.
/// </summary>
public class PaymentGatewayException : Exception
{
    public string? DebugId { get; }

    public PaymentGatewayException(string message, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        DebugId = debugId;
    }
}
