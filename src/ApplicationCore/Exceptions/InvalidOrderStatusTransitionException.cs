using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order is asked to move to a status that is not legal from its current one
/// (for example, dispatching an already-cancelled order).
/// </summary>
public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(OrderStatus from, OrderStatus to)
        : base($"An order cannot move from {from} to {to}.")
    {
    }
}
