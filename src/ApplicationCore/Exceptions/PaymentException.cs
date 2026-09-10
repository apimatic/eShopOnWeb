using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment operation cannot be completed for a business reason the
/// caller (shopper or operator) can act on — e.g. refunding more than was captured,
/// or an authorization that can no longer be renewed. Maps to HTTP 422.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message)
    {
    }

    public PaymentException(string message, Exception inner) : base(message, inner)
    {
    }
}
