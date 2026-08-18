using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(int orderId, OrderStatus from, OrderStatus to)
        : base($"Order {orderId} cannot move from {from} to {to}")
    {
    }
}
