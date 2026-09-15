using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Thrown when a payment lifecycle operation is attempted from a state that does not allow it
/// (for example, capturing an order that was never authorized, or cancelling one already fulfilled).
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(int orderId, OrderStatus status, string operation)
        : base($"Order {orderId} is in state '{status}', which does not allow the '{operation}' operation.")
    {
        OrderId = orderId;
        Status = status;
        Operation = operation;
    }

    public int OrderId { get; }
    public OrderStatus Status { get; }
    public string Operation { get; }
}
