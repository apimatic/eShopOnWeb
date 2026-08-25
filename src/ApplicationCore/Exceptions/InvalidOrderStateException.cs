using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when a payment operation is requested for an order that is not in a state that allows it.</summary>
public class InvalidOrderStateException : Exception
{
    public int OrderId { get; }
    public OrderStatus CurrentStatus { get; }

    public InvalidOrderStateException(int orderId, OrderStatus currentStatus, string attemptedAction)
        : base($"Order {orderId} cannot {attemptedAction} while it is in status '{currentStatus}'.")
    {
        OrderId = orderId;
        CurrentStatus = currentStatus;
    }
}
