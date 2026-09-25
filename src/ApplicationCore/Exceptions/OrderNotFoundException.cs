using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order does not exist, or is not owned by the caller. Maps to 404 at the API
/// boundary so that one shopper can never discover or act on another's order.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"No order found with id {orderId}")
    {
    }
}
