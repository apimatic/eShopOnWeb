using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order/payment is in a state that does not allow the requested
/// operation, or when PayPal reports the operation can no longer be performed.
/// Maps to HTTP 409 so an operator or shopper can act on the message.
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}
