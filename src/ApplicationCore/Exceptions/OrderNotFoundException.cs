using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order cannot be found for the caller. Also used when an order exists but
/// belongs to another shopper, so cross-owner access is indistinguishable from "not found".
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order with id {orderId} was found for the current user.")
    {
    }
}
