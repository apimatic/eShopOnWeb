using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends and tracks the SMS notifications tied to an order's lifecycle, and gives operators the
/// levers to act on them afterwards. None of the notify methods throw on a send failure.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Messages the shopper their order is on its way and schedules the "how did it go?" follow-up.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Calls off any pending follow-up for the order, then messages the shopper it was cancelled.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>The notifications for an order; refreshes their delivery outcome from the provider when asked.</summary>
    Task<IReadOnlyList<Notification>> GetNotificationsForOrderAsync(int orderId, bool refresh = true, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the delivery outcome of the given notifications from the provider (best effort).</summary>
    Task RefreshStatusesAsync(IReadOnlyCollection<Notification> notifications, CancellationToken cancellationToken = default);

    /// <summary>Operator action: re-sends a message that did not reach the shopper, idempotent on the given key. Null if the notification is not found.</summary>
    Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Operator action: disposes of a message's content at the provider and locally. Returns null if not found.</summary>
    Task<Notification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconciles the provider's record of sent messages against eShop's over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    Task<Notification?> GetByIdAsync(int notificationId, CancellationToken cancellationToken = default);
}
