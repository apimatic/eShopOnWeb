using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order does not exist, or exists but is not owned by the caller. The two cases
/// are deliberately indistinguishable so one shopper cannot probe for another's orders.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"No order found with id {orderId}")
    {
    }
}
