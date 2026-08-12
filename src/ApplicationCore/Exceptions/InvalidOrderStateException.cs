using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order lifecycle transition is not allowed from the order's current state
/// (for example, dispatching an already-cancelled order).
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(int orderId, OrderStatus current, OrderStatus attempted)
        : base($"Order {orderId} is {current} and cannot transition to {attempted}.")
    {
        OrderId = orderId;
        Current = current;
        Attempted = attempted;
    }

    public int OrderId { get; }
    public OrderStatus Current { get; }
    public OrderStatus Attempted { get; }
}
