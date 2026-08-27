using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment-related action is not valid for the order's current state.
/// Maps to HTTP 409 Conflict.
/// </summary>
public class OrderPaymentStateException : Exception
{
    public OrderPaymentStateException(string message) : base(message)
    {
    }
}
