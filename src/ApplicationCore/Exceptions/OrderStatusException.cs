using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order lifecycle transition (dispatch / cancel) is not allowed
/// from the order's current <see cref="Entities.OrderAggregate.OrderStatus"/>.
/// </summary>
public class OrderStatusException : Exception
{
    public OrderStatusException(string message) : base(message)
    {
    }
}
