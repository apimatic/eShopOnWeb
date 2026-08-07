using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the payment processor rejects or fails a request. The <see cref="Message"/> is safe to
/// surface to callers (it never contains card data); <see cref="DebugId"/> is PayPal's correlation id
/// for support/troubleshooting.
/// </summary>
public class PaymentGatewayException : Exception
{
    public string? DebugId { get; }

    public PaymentGatewayException(string message, string? debugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        DebugId = debugId;
    }
}
