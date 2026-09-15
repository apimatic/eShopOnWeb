using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order cannot be found — or, for shopper-scoped requests, does not belong to the
/// caller. The two cases are deliberately indistinguishable so one shopper cannot probe for another's
/// orders.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"No order found with id {orderId}.")
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}
