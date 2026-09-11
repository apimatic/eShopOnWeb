using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order does not exist or is not owned by the caller. The message is deliberately
/// uniform for both cases so ownership cannot be probed.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.")
    {
    }
}
