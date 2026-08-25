using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when PayPal rejects or cannot complete a payment operation. Carries only a
/// caller-safe message — never the raw SDK/provider exception text.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
