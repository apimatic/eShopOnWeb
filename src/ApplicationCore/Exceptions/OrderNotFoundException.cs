using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order does not exist, or does not belong to the calling shopper. Not-owned is
/// deliberately reported as not-found so one shopper cannot probe for another's orders.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"No order found with id {orderId}") { }
}
