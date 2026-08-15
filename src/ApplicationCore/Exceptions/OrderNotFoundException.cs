using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The order does not exist, or does not belong to the caller. The same exception is used for both
/// so a shopper cannot probe for the existence of another shopper's orders.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order found with id {orderId}.")
    {
    }
}
