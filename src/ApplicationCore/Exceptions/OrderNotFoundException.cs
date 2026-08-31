using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order cannot be found for the caller. Also used when the order exists but belongs
/// to a different shopper, so that ownership is never revealed to someone who does not own it.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order found with id {orderId}")
    {
    }
}
