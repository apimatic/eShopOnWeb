using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the SMS a shopper receives as an order moves, and the operator actions taken on
/// those messages afterwards. A message that cannot be sent never fails the underlying order
/// operation; a shopper with no number on file is simply not messaged.
/// </summary>
public interface INotificationService
{
    /// <summary>Tells the shopper their order was placed.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order is on its way and queues a "how did the delivery go?" follow-up
    /// with the provider for a few days later.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the shopper the order was cancelled and calls off any follow-up still queued with the
    /// provider so it can never reach them.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating under the same idempotency key
    /// returns the already-produced message without sending again; a fresh key sends a new message.
    /// Returns null if the original notification does not exist.
    /// </summary>
    Task<SmsNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider and locally. Returns null if the notification
    /// does not exist.
    /// </summary>
    Task<SmsNotification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Returns the order's notifications with their delivery outcomes refreshed from the provider.</summary>
    Task<IReadOnlyList<SmsNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the given notifications' delivery outcomes from the provider (best effort).</summary>
    Task RefreshStatusesAsync(IEnumerable<SmsNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls off any follow-up still queued with the provider for a number a shopper has just removed,
    /// so nothing is sent to that number again.
    /// </summary>
    Task CancelPendingForNumberAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lines up the provider's own record of messages from this app's sending number against what
    /// eShop believes it sent, over the whole range.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A reconciliation report over a date range for this app's sending number.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> OnlyAtProvider,
    IReadOnlyList<ReconciliationEntry> OnlyInEShop);

/// <summary>One reconciled message, present at the provider, in eShop, or both.</summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    string? ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset? DateSent,
    int? NotificationId,
    int? OrderId,
    string? EShopStatus);
