using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operator tries to move an order to a state it cannot reach from its current one
/// (e.g. dispatching a cancelled order, or dispatching one already dispatched).
/// </summary>
public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(OrderStatus from, OrderStatus to)
        : base($"An order that is {from} cannot be moved to {to}.")
    {
    }
}
