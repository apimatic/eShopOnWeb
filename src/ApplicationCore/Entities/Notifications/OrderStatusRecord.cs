using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

/// <summary>
/// Delivery-lifecycle overlay for an eShop <c>Order</c>. Created when an order is placed
/// through the API and advanced by the operator dispatch / cancel actions. Kept separate
/// from the <c>Order</c> aggregate so the notification feature stays purely additive.
/// </summary>
public class OrderStatusRecord : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderStatusRecord() { }
#pragma warning restore CS8618

    public OrderStatusRecord(int orderId, string buyerId)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        State = OrderDeliveryState.Placed;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderDeliveryState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkDispatched()
    {
        if (State == OrderDeliveryState.Cancelled)
            throw new InvalidOperationException($"Order {OrderId} has been cancelled and cannot be dispatched.");
        if (State == OrderDeliveryState.Dispatched)
            throw new InvalidOperationException($"Order {OrderId} has already been dispatched.");

        State = OrderDeliveryState.Dispatched;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCancelled()
    {
        if (State == OrderDeliveryState.Cancelled)
            throw new InvalidOperationException($"Order {OrderId} has already been cancelled.");

        State = OrderDeliveryState.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
