using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation is not valid for the payment's current state
/// (for example capturing a payment that was never authorized, or refunding more
/// than was captured). The message is intended to be safe to surface to an operator.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message)
    {
    }

    public PaymentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
