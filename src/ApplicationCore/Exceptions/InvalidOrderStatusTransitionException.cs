using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order is asked to move to a status it cannot reach from its current one
/// (for example dispatching an already-cancelled order). The API surfaces this as a 409 Conflict.
/// </summary>
public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(int orderId, OrderStatus from, OrderStatus to)
        : base($"Order {orderId} cannot move from {from} to {to}.")
    {
    }
}
