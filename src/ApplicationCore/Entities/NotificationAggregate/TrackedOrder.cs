using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Tracks the dispatch/cancel lifecycle of an order for the notifications feature, without
/// modifying the base <c>Order</c> aggregate. One row per order this app placed.
/// </summary>
public class TrackedOrder : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private TrackedOrder() { }
#pragma warning restore CS8618

    public TrackedOrder(int orderId, string buyerId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Status = OrderLifecycleStatus.Placed;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderLifecycleStatus Status { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>
    /// Attempts to move the order to Dispatched. Returns <c>true</c> only when the state actually
    /// changed (so the caller gates its outbound messages on a real transition, not a no-op).
    /// Throws when the order was already cancelled — a cancelled order must never be dispatched.
    /// </summary>
    public bool TryMarkDispatched()
    {
        if (Status == OrderLifecycleStatus.Cancelled)
            throw new InvalidOperationException("A cancelled order cannot be dispatched.");
        if (Status == OrderLifecycleStatus.Dispatched)
            return false;

        Status = OrderLifecycleStatus.Dispatched;
        return true;
    }

    /// <summary>
    /// Attempts to move the order to Cancelled. Returns <c>true</c> only when the state actually
    /// changed. Cancellation is allowed from either Placed or Dispatched.
    /// </summary>
    public bool TryMarkCancelled()
    {
        if (Status == OrderLifecycleStatus.Cancelled)
            return false;

        Status = OrderLifecycleStatus.Cancelled;
        return true;
    }
}
