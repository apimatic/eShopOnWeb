using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order cannot be found — or, for shopper-scoped requests, when it belongs to another shopper.
/// The two cases are deliberately indistinguishable so that one shopper cannot probe for another's orders.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"No order with id {orderId} was found.")
    {
    }
}
