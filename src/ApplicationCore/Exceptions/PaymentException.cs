using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation is not valid for the current state of an order
/// (for example capturing an order that was never authorized, or refunding more than
/// was captured). Maps to HTTP 409 Conflict at the API boundary.
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
