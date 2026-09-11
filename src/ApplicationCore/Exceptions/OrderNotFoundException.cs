using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order (or its payment) cannot be found for the caller. The same exception
/// is used whether the order truly does not exist or simply belongs to another shopper, so a
/// shopper can never probe for the existence of another's order.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"No order found with id {orderId}.")
    {
    }
}
