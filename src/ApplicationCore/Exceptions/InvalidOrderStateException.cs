using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order-lifecycle transition is not legal for the order's current state
/// (e.g. dispatching an already-cancelled order). Maps to HTTP 409.
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(int orderId, OrderStatus currentStatus, string attemptedAction)
        : base($"Order {orderId} is '{currentStatus}' and cannot undergo '{attemptedAction}'.")
    {
    }
}
