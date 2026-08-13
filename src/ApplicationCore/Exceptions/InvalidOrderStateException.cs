using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order is asked to make a transition that is not allowed from its current
/// <see cref="OrderStatus"/> (for example dispatching an order that has already been cancelled).
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(int orderId, OrderStatus current, string attemptedTransition)
        : base($"Order {orderId} cannot be {attemptedTransition} while it is {current}.")
    {
    }
}
