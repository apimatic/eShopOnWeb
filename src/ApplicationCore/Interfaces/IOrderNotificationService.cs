using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates orders and the SMS notifications that go out as an order moves. A message that
/// cannot be sent never fails the underlying order operation; a shopper with no number on file is
/// simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Place an order for the shopper from catalog item ids and quantities, reusing the app's
    /// order/order-item model, then tell the shopper it was placed. Returns the new order id.
    /// </summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: mark the order dispatched, tell the shopper it is on its way, and queue a
    /// follow-up with the provider to go out a few days later asking how the delivery went.
    /// </summary>
    Task DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: cancel the order, tell the shopper, and call off any not-yet-sent follow-up
    /// so the "how did delivery go?" message never reaches them.
    /// </summary>
    Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: re-send a message that did not reach the shopper. The idempotency key makes a
    /// repeat under the same key a no-op (no second message); a fresh key is a genuine new attempt.
    /// Returns the id of the notification the resend produced.
    /// </summary>
    Task<int> ResendNotificationAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider so its text is no longer retrievable there,
    /// while the fact that it was sent and what became of it survives.
    /// </summary>
    Task DisposeNotificationContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconcile the provider's own record of messages from the configured sending number over a
    /// date range against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Load the notifications for an order, refreshing any non-terminal delivery outcomes from the
    /// provider first. Returns null if the order does not exist.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Load all of a shopper's notifications, refreshing non-terminal delivery outcomes from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetBuyerNotificationsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Fetch a single notification by id (no provider refresh), or null if not found.</summary>
    Task<OrderNotification?> FindNotificationAsync(int notificationId, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>The reconciliation report: provider records lined up against eShop's own records.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string SendingNumber,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>One line of the reconciliation report.</summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    string? ProviderStatus,
    int? NotificationId,
    int? OrderId,
    string? EShopStatus,
    DateTimeOffset? DateSent);
