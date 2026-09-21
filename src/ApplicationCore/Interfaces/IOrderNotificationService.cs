using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order lifecycle and the SMS notifications that accompany it. Notification sends
/// are best-effort: a message that cannot be sent is recorded as a failed notification and never
/// fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order for the shopper from catalog items and tells them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken ct);

    /// <summary>
    /// Operator action: marks the order dispatched, tells the shopper it is on its way, and queues a
    /// "how did the delivery go?" follow-up with the provider for a few days later. Returns false if
    /// no such order exists.
    /// </summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Operator action: cancels the order, tells the shopper, and calls off any not-yet-sent
    /// follow-up so it can never reach them. Returns false if no such order exists.
    /// </summary>
    Task<bool> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Operator action: re-sends a message that did not reach the shopper. The caller-supplied
    /// idempotency key makes a repeat a no-op (returns the notification the first attempt produced),
    /// while a fresh key sends again. Returns null if the source notification does not exist.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Operator action: disposes of a message's content so it is no longer retrievable at the provider,
    /// while the fact it was sent and its outcome survive. Returns false if the notification does not
    /// exist or never reached the provider.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct);

    /// <summary>
    /// Operator action: reconciles the provider's own record of messages from the configured sending
    /// number over a range against what this application believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>The shopper's orders, each with its notifications (statuses refreshed from the provider).</summary>
    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>
    /// The notifications for one order, if it belongs to the shopper (statuses refreshed). Null when
    /// the order does not exist or is not the caller's.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsForBuyerAsync(
        int orderId, string buyerId, CancellationToken ct);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>An order paired with the notifications produced for it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>One line of a reconciliation report.</summary>
public record ReconciliationEntry(
    string? Sid,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    int? NotificationId,
    string? EShopStatus,
    NotificationKind? Kind);

/// <summary>
/// The result of lining up the provider's record against eShop's for a range: messages both agree on,
/// messages the provider knows but eShop does not, and messages eShop recorded but the provider did
/// not return.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
