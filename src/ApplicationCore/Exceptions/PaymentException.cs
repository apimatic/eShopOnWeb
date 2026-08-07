using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment-related domain rule is violated (for example an invalid
/// payment-state transition, or attempting to pay an order that cannot be paid).
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
