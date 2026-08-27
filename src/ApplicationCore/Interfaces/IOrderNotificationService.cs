using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    string? Status,
    DateTimeOffset? Date,
    string MatchState); // "matched", "providerOnly", "eshopOnly"

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderMessageCount,
    int EshopNotificationCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Entries);

/// <summary>
/// Orchestrates order SMS notifications. Messaging failures never fail the underlying
/// order operation; they are recorded on the notification instead.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the provider-owned delivery outcome of the given notifications.</summary>
    Task RefreshStatusesAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message of a failed notification. Repeating under an already-used
    /// idempotency key returns the original re-send without messaging again.
    /// Returns null when the notification does not exist.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text both locally and at the provider. Returns null when unknown.</summary>
    Task<OrderNotification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
