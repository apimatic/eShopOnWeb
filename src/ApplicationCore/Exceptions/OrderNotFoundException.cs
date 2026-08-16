using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order does not exist, or exists but is not owned by the caller. The same
/// exception is used for both cases so a shopper cannot probe for the existence of another
/// shopper's orders. Surfaces as a 404 Not Found.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.")
    {
    }
}
