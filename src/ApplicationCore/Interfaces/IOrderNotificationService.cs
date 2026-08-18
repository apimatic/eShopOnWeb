using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One requested line of an order: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Outcome of placing an order.</summary>
public record PlaceOrderResult(bool Success, int OrderId, string? Error)
{
    public static PlaceOrderResult Ok(int orderId) => new(true, orderId, null);
    public static PlaceOrderResult Fail(string error) => new(false, 0, error);
}

/// <summary>Outcome of an operator re-send.</summary>
public record ResendResult(bool Found, bool Deduplicated, int NotificationId, string? Error)
{
    public static ResendResult NotFound() => new(false, false, 0, "Notification not found.");
    public static ResendResult Resent(int notificationId) => new(true, false, notificationId, null);
    public static ResendResult Duplicate(int notificationId) => new(true, true, notificationId, null);
}

/// <summary>An order together with the notifications sent about it.</summary>
public record OrderNotificationsView(Order Order, IReadOnlyList<OrderNotification> Notifications);

/// <summary>One line of the reconciliation report: a message and where it is known.</summary>
public record ReconciliationEntry(
    string? Sid,
    string? Status,
    int? ErrorCode,
    DateTimeOffset? DateSent,
    int? NotificationId,
    NotificationKind? Kind);

/// <summary>
/// The provider's record of messages for a date range lined up against what eShop believes it
/// sent, so a message one side knows about and the other does not is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>
/// Places orders and keeps shoppers informed by SMS as those orders move, and gives operators
/// the tools to act on what actually reached the customer. A message that cannot be sent never
/// fails the underlying operation; a shopper with no number on file is simply not messaged.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Places an order for the shopper from catalog item ids and quantities, reusing the app's
    /// existing order model, and tells the shopper their order was placed. Returns the new order id.
    /// </summary>
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an order dispatched (operator action): tells the shopper it is on its way and queues
    /// a "how did delivery go?" follow-up with the provider for a few days later. Returns false if
    /// the order does not exist.
    /// </summary>
    Task<bool> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an order (operator action): tells the shopper, and calls off any not-yet-sent
    /// follow-up so it can never reach them. Returns false if the order does not exist.
    /// </summary>
    Task<bool> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders, each with the notifications sent about it (statuses refreshed).</summary>
    Task<IReadOnlyList<OrderNotificationsView>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The notifications sent for one of the caller's orders, with delivery outcomes refreshed.
    /// Returns null if the order does not exist or does not belong to the caller.
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper (operator action). The caller-supplied
    /// idempotency key makes a repeat of the same request a no-op that returns the message the
    /// first attempt produced, while a fresh key sends again.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the shopper's request (operator action): redacts the
    /// body at the provider so its text is no longer retrievable there either, while the fact the
    /// message was sent and what became of it survives. Returns false if the notification does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Builds the reconciliation report over the given range (operator action).</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
