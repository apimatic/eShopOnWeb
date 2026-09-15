using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order is asked to make a lifecycle transition that is not allowed
/// (for example dispatching an order that has already been cancelled).
/// </summary>
public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(OrderStatus from, OrderStatus to)
        : base($"An order in status '{from}' cannot be moved to '{to}'.")
    {
        From = from;
        To = to;
    }

    public OrderStatus From { get; }
    public OrderStatus To { get; }
}
