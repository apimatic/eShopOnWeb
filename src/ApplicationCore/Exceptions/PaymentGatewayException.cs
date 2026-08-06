using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the external payment gateway rejects or fails a request (e.g. a declined card).
/// Carries a caller-safe message; it never contains card details.
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
