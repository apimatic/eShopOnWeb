using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum ResendStatus
{
    /// <summary>A fresh message was sent.</summary>
    Sent,
    /// <summary>The idempotency key had already been used; the earlier notification is returned and nothing new was sent.</summary>
    Duplicate,
    /// <summary>The notification to re-send does not exist.</summary>
    OriginalNotFound,
    /// <summary>The original message already reached the shopper, so there is nothing to re-send.</summary>
    AlreadyDelivered
}

public class ResendResult
{
    public required ResendStatus Status { get; init; }
    public OrderNotification? Notification { get; init; }
}

/// <summary>
/// Sends the messages that go out as an order moves, and gives the operator the levers over them.
/// A message that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tell the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tell the shopper the order is on its way, and queue a delivery follow-up with the provider for a few days later.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tell the shopper the order was cancelled, and call off the delivery follow-up so it never reaches them.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>All messages sent for an order, each with its up-to-date provider outcome.</summary>
    Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The up-to-date notifications for several orders at once (used by the shopper's order list).</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<OrderNotification>>> GetForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default);

    /// <summary>Operator re-send of a message that did not reach the shopper, made idempotent by the caller's key.</summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Dispose of a message's content at the provider on the shopper's request. Returns false if the notification is unknown.</summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Reconcile the provider's ledger against eShop's records for a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
