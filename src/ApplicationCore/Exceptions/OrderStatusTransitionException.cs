using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderStatusTransitionException : Exception
{
    public OrderStatusTransitionException(int orderId, OrderStatus current, OrderStatus target)
        : base($"Order {orderId} cannot move from '{current}' to '{target}'.")
    {
        OrderId = orderId;
        Current = current;
        Target = target;
    }

    public int OrderId { get; }
    public OrderStatus Current { get; }
    public OrderStatus Target { get; }
}
