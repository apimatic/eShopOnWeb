using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(OrderStatus current, OrderStatus requested)
        : base($"Order cannot move from {current} to {requested}.")
    {
        Current = current;
        Requested = requested;
    }

    public OrderStatus Current { get; }
    public OrderStatus Requested { get; }
}
