using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application service that ties the order lifecycle to its SMS notifications. Placing, dispatching
/// and cancelling an order each succeed regardless of whether a message could be sent — a messaging
/// failure never fails the underlying operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Place an order for the shopper from catalog items, reusing the existing order model. Sends an "order placed" SMS.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken ct = default);

    /// <summary>Operator action: mark an order dispatched, tell the shopper, and schedule the delivery follow-up.</summary>
    Task<Order> DispatchOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancel an order, tell the shopper, and call off any not-yet-sent follow-up.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>The caller's own orders, each with its notifications' current delivery outcomes.</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default);

    /// <summary>An order, if it exists (used for ownership checks).</summary>
    Task<Order?> GetOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// The notifications for an order, with their delivery outcomes refreshed from the provider.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken ct = default);

    /// <summary>A single notification, if it exists.</summary>
    Task<OrderNotification?> GetNotificationAsync(int notificationId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: re-send a notification that did not reach the shopper. The idempotency key makes
    /// a repeated request under the same key a no-op that returns the same result; a fresh key sends again.
    /// Returns the notification the resend produced.
    /// </summary>
    Task<OrderNotification> ResendNotificationAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Operator action: dispose of a message's content at the provider and locally. The record survives.</summary>
    Task DisposeNotificationContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: reconcile the provider's own record of messages from the configured sending number
    /// against what the shop believes it sent, over a date range.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>One requested order line: a catalog item and a quantity.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order together with its notifications (with delivery outcomes refreshed from the provider).</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);
