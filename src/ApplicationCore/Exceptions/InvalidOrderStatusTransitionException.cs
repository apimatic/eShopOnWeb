using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Thrown when an order is asked to move to a status that is not reachable from its current one
/// (for example, cancelling an already-dispatched order).
/// </summary>
public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(OrderStatus from, OrderStatus to)
        : base($"An order in status '{from}' cannot transition to '{to}'.")
    {
        From = from;
        To = to;
    }

    public OrderStatus From { get; }
    public OrderStatus To { get; }
}
