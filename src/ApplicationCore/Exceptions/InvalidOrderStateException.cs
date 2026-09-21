using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order cannot make the requested transition from its current status
/// (e.g. dispatching a cancelled order). Surfaced as HTTP 409 Conflict.
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(int orderId, OrderStatus currentStatus, string attemptedTransition)
        : base($"Order {orderId} is {currentStatus} and cannot be transitioned by {attemptedTransition}.")
    {
    }
}
