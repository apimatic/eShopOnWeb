using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order cannot be found for the current shopper — either it does not exist or it
/// belongs to a different shopper. The two cases are deliberately indistinguishable to callers so a
/// shopper cannot probe for the existence of another's orders.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order found with id {orderId}")
    {
    }
}
