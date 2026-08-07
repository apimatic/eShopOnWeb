using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order does not exist, or does not belong to the caller. The two are deliberately
/// indistinguishable to the caller so one shopper cannot probe for another's order ids.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order found with id {orderId} for the current shopper.")
    {
    }
}
