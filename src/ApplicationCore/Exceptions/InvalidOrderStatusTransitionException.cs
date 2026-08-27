using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(OrderStatus current, OrderStatus target)
        : base($"Order cannot transition from {current} to {target}")
    {
        Current = current;
        Target = target;
    }

    public OrderStatus Current { get; }
    public OrderStatus Target { get; }
}
