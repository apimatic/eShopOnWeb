using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the messages that go out as an order moves, and the operator actions
/// (resend, content disposal, reconciliation) on top of them. Sending a message never
/// throws back into the underlying order operation: a message that cannot be sent is
/// recorded as such, and the order operation still succeeds.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed. Never throws.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order is on its way and queues a "how did delivery go?" follow-up
    /// with the provider for a few days later. Never throws.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls off any not-yet-sent delivery follow-up for the order, then tells the shopper the
    /// order was cancelled. Never throws.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator resend of a message that did not reach the shopper. The idempotency key makes a
    /// repeated request a no-op (returns the same produced notification) while a fresh key sends
    /// again. Returns null if the referenced notification does not exist.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider and locally. The record of the message
    /// and its outcome survives. Returns false if the notification does not exist.
    /// </summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Notifications for one order, refreshed against the provider's current state.</summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Notifications for a shopper (across their orders), refreshed against the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetNotificationsForOwnerAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lines the provider's own record of messages for a range up against what eShop believes it
    /// sent, counting only messages from the application's configured sending number.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

/// <summary>One line of a reconciliation report.</summary>
public record ReconciliationEntry(
    string? Sid,
    string Presence,
    string? ProviderStatus,
    string? ProviderTo,
    DateTimeOffset? ProviderDateSent,
    int? NotificationId,
    int? OrderId,
    string? Kind,
    string? EShopStatus);

/// <summary>
/// A reconciliation report over a date range. Presence values on entries are
/// "matched", "provider_only" (provider knows, eShop doesn't) and "eshop_only" (the reverse).
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
