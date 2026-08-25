using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when the requested action is not valid for the order's current status,
/// e.g. fulfilling an order that was never authorized, or refunding one that was cancelled.</summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(int orderId, OrderStatus currentStatus, string attemptedAction)
        : base($"Cannot {attemptedAction} order {orderId}: it is currently {currentStatus}.")
    {
        OrderId = orderId;
        CurrentStatus = currentStatus;
    }

    public InvalidOrderStateException(string message) : base(message)
    {
    }

    public int OrderId { get; }
    public OrderStatus CurrentStatus { get; }
}
