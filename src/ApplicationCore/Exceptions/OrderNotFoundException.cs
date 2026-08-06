using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order does not exist, or does not belong to the requesting shopper.
/// A shopper must never learn about another shopper's orders, so a not-owned order is
/// reported exactly like a missing one.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"No order found with id {orderId}")
    {
    }
}
