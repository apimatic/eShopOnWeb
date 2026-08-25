using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment action is attempted against an order that is not in a valid state for it
/// (e.g. fulfilling an order that hasn't been authorized, or refunding one that hasn't been captured).
/// </summary>
public class OrderPaymentStateException : Exception
{
    public OrderPaymentStateException(string message) : base(message)
    {
    }

    public OrderPaymentStateException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
