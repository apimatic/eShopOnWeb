using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends and tracks the SMS messages that go out as an order moves, and gives operators the
/// levers over them (resend, content disposal, reconciliation). None of the "notify" methods may
/// throw on a messaging failure — the underlying order operation must still succeed.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tell the shopper the order is on its way and queue the "how did delivery go?" follow-up with the provider for a few days later.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tell the shopper the order was cancelled and call off any follow-up that has not yet gone out.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Notifications for a single order, each refreshed against the provider's current delivery outcome.</summary>
    Task<IReadOnlyList<SmsNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator re-sends a message that did not reach the shopper, idempotent under the caller-supplied key.</summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Operator disposes of a message's content at the shopper's request — unrecoverable at the provider too, while the record and status survive.</summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Operator reconciliation over a date range: the provider's own record for eShop's sending number, lined up against eShop's records.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
