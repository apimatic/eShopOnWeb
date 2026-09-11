using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment operation cannot proceed for a business reason an operator or shopper can
/// act on — for example attempting to fulfil an order that is not authorized, refunding beyond the
/// captured amount, or an authorization that has expired and can no longer be renewed. The message
/// is safe to surface to the caller.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }

    public PaymentException(string message, Exception innerException) : base(message, innerException) { }
}
