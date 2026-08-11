using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order cannot be found for the requested id (or does not belong to the caller).
/// The same "not found" is used for another shopper's order so ownership is never leaked.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order found with id {orderId}.")
    {
    }
}
