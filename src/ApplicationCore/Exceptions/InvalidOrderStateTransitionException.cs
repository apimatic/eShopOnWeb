using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order is asked to move to a state it cannot legally reach from its current one
/// (for example dispatching an already-cancelled order).
/// </summary>
public class InvalidOrderStateTransitionException : Exception
{
    public InvalidOrderStateTransitionException(int orderId, OrderStatus from, OrderStatus to)
        : base($"Order {orderId} cannot move from {from} to {to}.")
    {
    }

    public InvalidOrderStateTransitionException(string message) : base(message)
    {
    }

    public InvalidOrderStateTransitionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
