using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the payment gateway (PayPal) rejects a request or is unreachable. The message is
/// safe to surface; it never contains card data.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message) : base(message)
    {
    }

    public PaymentGatewayException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
