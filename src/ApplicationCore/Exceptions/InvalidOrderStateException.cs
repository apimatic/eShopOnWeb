using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operator action is attempted against an order whose current status does not
/// permit it (for example dispatching an already-dispatched order, or cancelling a cancelled one).
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(int orderId, OrderStatus currentStatus, string attemptedAction)
        : base($"Order {orderId} cannot be {attemptedAction}ed while it is {currentStatus}.")
    {
    }
}
