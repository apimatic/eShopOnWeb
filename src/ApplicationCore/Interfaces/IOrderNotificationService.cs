using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends order-related SMS notifications and keeps the local record of what was
/// sent in step with the provider. Sending failures never propagate: the
/// underlying order operation always succeeds.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Returns the notifications for an order, refreshing non-final delivery outcomes from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Returns the notifications for the given orders, refreshing non-final delivery outcomes from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message of a notification that did not reach the shopper.
    /// Repeating a request under an idempotency key that was already used returns the
    /// message the first request produced without sending again.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a notification's message text, both locally and at the provider.</summary>
    Task<OrderNotification> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Lines up the provider's record of messages for a range against the local record.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class ResendResult
{
    public OrderNotification Notification { get; set; } = default!;

    /// <summary>True when the idempotency key was already used and no new message was sent.</summary>
    public bool WasDuplicate { get; set; }
}
