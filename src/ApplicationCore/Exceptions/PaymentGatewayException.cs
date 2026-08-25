using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

// Wraps a PayPal-reported error or a connectivity failure encountered while talking to the payment
// gateway. The message is always caller-safe (never the raw SDK/provider exception text).
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message) : base(message)
    {
    }

    public PaymentGatewayException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
