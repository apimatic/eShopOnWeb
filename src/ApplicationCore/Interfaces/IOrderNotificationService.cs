using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Ties orders to the SMS messages that go out as they move. A message that cannot be sent never
/// fails the underlying order operation — the order is still placed, dispatched or cancelled.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Places an order from catalog lines for the shopper and tells them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken);

    /// <summary>Marks an order dispatched, tells the shopper it is on its way, and queues a delivery follow-up for a few days later.</summary>
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Cancels an order, tells the shopper, and calls off any follow-up not yet sent.</summary>
    Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>The caller's orders. Notification delivery outcomes are refreshed from the provider.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>All of the caller's notifications (across their orders), delivery outcomes refreshed from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForBuyerAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>
    /// Notifications for an order, delivery outcomes refreshed from the provider. When <paramref name="buyerIdScope"/>
    /// is supplied, only that shopper's order is visible (returns null when the order is not theirs / not found).
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetNotificationsForOrderAsync(int orderId, string? buyerIdScope, CancellationToken cancellationToken);

    /// <summary>Re-sends a message that did not reach the shopper, idempotent under the supplied key.</summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Disposes of a message's content at the provider and locally, leaving the record and its outcome.</summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);

    /// <summary>Reconciles the provider's own record of messages from the configured sending number against eShop's.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Outcome of a resend.</summary>
public record ResendResult(bool OriginalFound, int? NotificationId, bool Reused, string? Error)
{
    public static ResendResult NotFound() => new(false, null, false, null);
    public static ResendResult Sent(int notificationId) => new(true, notificationId, false, null);
    public static ResendResult ReusedExisting(int notificationId) => new(true, notificationId, true, null);
    public static ResendResult Failed(string error) => new(true, null, false, error);
}

/// <summary>How an entry lines up between the provider's record and eShop's.</summary>
public enum ReconciliationMatch
{
    InBoth = 0,
    ProviderOnly = 1,
    EShopOnly = 2
}

/// <summary>One reconciled message, seen from the provider, from eShop, or both.</summary>
public record ReconciliationEntry(
    ReconciliationMatch Match,
    string? ProviderMessageSid,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    int? NotificationId,
    int? OrderId,
    string? EShopStatus);

/// <summary>A reconciliation report over a date range for the configured sending number.</summary>
public record ReconciliationReport(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    int InBothCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
