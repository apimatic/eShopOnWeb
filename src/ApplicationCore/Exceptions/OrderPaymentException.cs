using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation is attempted from an invalid state, or would
/// violate an invariant (e.g. refunding more than was captured). The message is
/// safe to surface to an operator/shopper.
/// </summary>
public class OrderPaymentException : Exception
{
    public OrderPaymentException(string message) : base(message)
    {
    }

    public OrderPaymentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
