using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment business rule was violated — for example paying an order that is not awaiting
/// payment, refunding beyond what was captured, or fulfilling a hold that can no longer be
/// renewed. Maps to a 409 Conflict so a caller (or operator) can act on it.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
}

/// <summary>
/// The requested order does not exist, or does not belong to the caller. The two are surfaced
/// identically (404) so one shopper cannot probe for another's orders.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order found with id {orderId}") { }
}

/// <summary>
/// The requested saved card does not exist, or does not belong to the caller.
/// </summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved card found with id {paymentMethodId}") { }
}
