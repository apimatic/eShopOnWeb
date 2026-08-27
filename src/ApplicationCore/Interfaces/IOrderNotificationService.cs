using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ResendResult
{
    public OrderNotification Notification { get; set; } = null!;
    public bool Replayed { get; set; }
}

public class ReconciliationEntry
{
    public string? MessageSid { get; set; }
    public int? NotificationId { get; set; }
    public string? To { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateSent { get; set; }

    /// <summary>
    /// Matched = known to both sides; ProviderOnly = the provider recorded a message
    /// eShop has no record of; ShopOnly = eShop recorded a message the provider does not list.
    /// </summary>
    public string Match { get; set; } = string.Empty;
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int ShopOnlyCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
}

/// <summary>
/// Orchestrates shopper notifications as orders move. Every operation is best-effort
/// with respect to the provider: a messaging failure never fails the underlying
/// order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends the message of a notification that did not reach the shopper.
    /// A repeated call under an already-seen idempotency key returns the notification
    /// the first call produced without sending again (Replayed = true).
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content both locally and at the provider, keeping the
    /// record that a message was sent and its outcome.
    /// </summary>
    Task DeleteContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls the current delivery outcome from the provider for every non-terminal
    /// notification of the order.
    /// </summary>
    Task RefreshOrderNotificationStatusesAsync(int orderId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
