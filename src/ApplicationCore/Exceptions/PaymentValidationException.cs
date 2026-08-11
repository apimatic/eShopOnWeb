using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment request is not valid for the current state of an order — for example
/// paying an order that is not awaiting payment, refunding more than was captured, or fulfilling
/// an order whose hold can no longer be renewed. The message is safe to surface to the caller.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}
