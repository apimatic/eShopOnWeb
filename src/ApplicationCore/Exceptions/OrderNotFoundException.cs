using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order cannot be found for the caller. Also used when an order exists but belongs to
/// another shopper, so one shopper cannot learn of another's order.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order with id {orderId} was found.")
    {
    }

    public OrderNotFoundException(string message) : base(message)
    {
    }

    public OrderNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
