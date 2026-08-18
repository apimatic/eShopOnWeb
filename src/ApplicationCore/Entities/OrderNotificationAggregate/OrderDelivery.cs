using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// Tracks the dispatch/cancel lifecycle of an order placed through the API. This is an additive
/// satellite of the existing <c>Order</c> aggregate (referenced by <see cref="OrderId"/>) so the
/// established order/order-item model is reused unchanged, while dispatch and cancellation — new
/// notions this feature introduces — get a home of their own.
/// </summary>
public class OrderDelivery : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderDelivery() { }
#pragma warning restore CS8618

    public OrderDelivery(int orderId, string ownerId)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        OrderId = orderId;
        OwnerId = ownerId;
        State = OrderDeliveryState.Placed;
    }

    public int OrderId { get; private set; }
    public string OwnerId { get; private set; }
    public OrderDeliveryState State { get; private set; }
    public DateTimeOffset? DispatchedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public void MarkDispatched()
    {
        if (State == OrderDeliveryState.Cancelled)
            throw new OrderLifecycleException($"Order {OrderId} has been cancelled and cannot be dispatched.");
        if (State == OrderDeliveryState.Dispatched)
            throw new OrderLifecycleException($"Order {OrderId} has already been dispatched.");

        State = OrderDeliveryState.Dispatched;
        DispatchedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCancelled()
    {
        if (State == OrderDeliveryState.Cancelled)
            throw new OrderLifecycleException($"Order {OrderId} has already been cancelled.");

        State = OrderDeliveryState.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }
}
