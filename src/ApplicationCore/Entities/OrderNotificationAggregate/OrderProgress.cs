using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// Tracks where an order has got to for notification purposes (placed, dispatched, cancelled).
/// Kept as its own aggregate so the existing <see cref="OrderAggregate.Order"/> model is left
/// untouched. One record per order.
/// </summary>
public class OrderProgress : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderProgress() { }
#pragma warning restore CS8618

    public OrderProgress(int orderId, string buyerId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Status = OrderProgressStatus.Placed;
        PlacedDate = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderProgressStatus Status { get; private set; }
    public DateTimeOffset PlacedDate { get; private set; }
    public DateTimeOffset? DispatchedDate { get; private set; }
    public DateTimeOffset? CancelledDate { get; private set; }

    public void MarkDispatched()
    {
        if (Status == OrderProgressStatus.Cancelled)
            throw new OrderNotificationConflictException($"Order {OrderId} was cancelled and cannot be dispatched.");
        if (Status == OrderProgressStatus.Dispatched)
            throw new OrderNotificationConflictException($"Order {OrderId} has already been dispatched.");

        Status = OrderProgressStatus.Dispatched;
        DispatchedDate = DateTimeOffset.UtcNow;
    }

    public void MarkCancelled()
    {
        if (Status == OrderProgressStatus.Cancelled)
            throw new OrderNotificationConflictException($"Order {OrderId} has already been cancelled.");

        Status = OrderProgressStatus.Cancelled;
        CancelledDate = DateTimeOffset.UtcNow;
    }
}
