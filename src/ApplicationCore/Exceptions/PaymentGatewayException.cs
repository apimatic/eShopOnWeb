using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal could not be reached, or returned a response this integration could not process
/// (a malformed body, or an unexpected transport failure). The message is deliberately generic
/// and safe to surface to a caller; details belong in the inner exception/logs only.
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
