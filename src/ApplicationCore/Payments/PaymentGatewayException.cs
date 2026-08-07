using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raised when the payment gateway rejects or fails a request. The message is sanitised — it carries
/// the gateway's error name/description and debug id for support, but never card data.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? debugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        DebugId = debugId;
    }

    /// <summary>PayPal debug_id, useful when contacting PayPal support to trace a failed call.</summary>
    public string? DebugId { get; }
}
