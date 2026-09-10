using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operation is attempted against an order whose current
/// <see cref="OrderStatus"/> does not permit it (e.g. fulfilling an unpaid order).
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(int orderId, OrderStatus status, string operation)
        : base($"Order {orderId} is in state '{status}', which does not allow operation '{operation}'.")
    {
    }

    public InvalidOrderStateException(string message) : base(message)
    {
    }
}
