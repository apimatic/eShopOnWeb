using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates orders and the SMS notifications that go out as they move. Sending is always
/// best-effort: a message that cannot be sent never fails the underlying order operation, and a
/// shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for <paramref name="buyerId"/> from catalog item selections (reusing the app's
    /// existing order/order-item model) and tells the shopper their order was placed.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineSelection> lines, CancellationToken ct);

    /// <summary>
    /// Marks the order dispatched, tells the shopper it is on its way, and queues a "how did the
    /// delivery go?" follow-up with the provider for a few days later. Returns null if no such order.
    /// </summary>
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Cancels the order, tells the shopper, and calls off any follow-up that has not yet gone out.
    /// Returns null if no such order.
    /// </summary>
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken ct);

    /// <summary>Returns the notifications for one order, with delivery outcomes refreshed from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken ct);

    /// <summary>Returns all notifications belonging to one shopper, with delivery outcomes refreshed from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetBuyerNotificationsAsync(string buyerId, CancellationToken ct);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating the request under the same
    /// <paramref name="idempotencyKey"/> does not send a second message (the first result is returned);
    /// a fresh key is a legitimate new attempt. Returns null if no such notification.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Disposes of a notification's message content at the provider and locally. The fact that the
    /// message was sent, and what became of it, survives. Returns null if no such notification.
    /// </summary>
    Task<OrderNotification?> RedactContentAsync(int notificationId, CancellationToken ct);

    /// <summary>
    /// Lines up the provider's own record of messages (sent from the configured sending number) over
    /// a date range against what eShop believes it sent, surfacing either-way discrepancies.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>A catalog item and quantity chosen when placing an order.</summary>
public record OrderLineSelection(int CatalogItemId, int Quantity);

/// <summary>The reconciliation of provider records against eShop records for a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderMessageCount,
    int EShopMessageCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>One line of a reconciliation report, keyed by the provider message SID where known.</summary>
public record ReconciliationEntry(
    string? Sid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? EShopStatus,
    DateTimeOffset? DateSent);
