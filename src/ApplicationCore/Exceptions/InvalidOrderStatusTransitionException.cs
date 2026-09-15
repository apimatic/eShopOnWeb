using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order is asked to move to a status it cannot legally reach from its current one
/// (for example, dispatching an order that has already been cancelled).
/// </summary>
public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(int orderId, OrderStatus from, OrderStatus to)
        : base($"Order {orderId} cannot move from {from} to {to}.")
    {
    }

    public InvalidOrderStatusTransitionException(string message) : base(message)
    {
    }

    public InvalidOrderStatusTransitionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
