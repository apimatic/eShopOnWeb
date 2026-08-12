using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order lifecycle transition is not allowed from the order's current state
/// (for example, dispatching an already-cancelled order).
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(int orderId, OrderStatus currentStatus, string attemptedTransition)
        : base($"Order {orderId} is {currentStatus} and cannot transition to {attemptedTransition}.")
    {
    }
}
