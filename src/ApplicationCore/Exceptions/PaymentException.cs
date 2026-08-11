using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation was invalid for the current state of the order/payment
/// (e.g. paying an order that is already authorized, refunding beyond the captured amount).
/// Surfaces to the caller as a 409/400.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
    public PaymentException(string message, Exception innerException) : base(message, innerException) { }
}
