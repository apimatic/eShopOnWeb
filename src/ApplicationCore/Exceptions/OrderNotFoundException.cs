using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order does not exist, or does not belong to the caller. The same exception is
/// used for "not found" and "not yours" so one shopper cannot probe for another's order ids.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order with id {orderId} was found.")
    {
    }
}
