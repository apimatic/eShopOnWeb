using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(OrderStatus current, OrderStatus target)
        : base($"Cannot transition an order from '{current}' to '{target}'.")
    {
        Current = current;
        Target = target;
    }

    public OrderStatus Current { get; }
    public OrderStatus Target { get; }
}
