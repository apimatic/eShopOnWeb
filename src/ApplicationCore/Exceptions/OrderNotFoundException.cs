using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order does not exist or does not belong to the caller. The two cases are
/// deliberately indistinguishable so one shopper cannot probe for another's orders.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order with id {orderId} was found for the current user.")
    {
    }
}
