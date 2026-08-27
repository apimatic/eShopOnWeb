using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum ResendOutcome
{
    Sent,
    Duplicate,
    NotFound,
    ContentDisposed,
    DestinationRemoved
}

public class ResendNotificationResult
{
    public ResendNotificationResult(ResendOutcome outcome, OrderNotification? notification)
    {
        Outcome = outcome;
        Notification = notification;
    }

    public ResendOutcome Outcome { get; }
    public OrderNotification? Notification { get; }
}

public enum ReconciliationMatch
{
    Matched,
    MissingLocally,
    MissingAtProvider
}

public class ReconciliationEntry
{
    public string? MessageSid { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? To { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public ReconciliationMatch Match { get; set; }
}

public class NotificationReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
    public int MatchedCount { get; set; }
    public int MissingLocallyCount { get; set; }
    public int MissingAtProviderCount { get; set; }
}

/// <summary>
/// Sends and tracks order SMS notifications. Messaging failures never fail the
/// underlying order operation; shoppers with no registered number are simply
/// not messaged.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Cancels any provider-queued (scheduled) messages for a contact number that is being removed.</summary>
    Task CancelScheduledMessagesForContactNumberAsync(int contactNumberId, CancellationToken cancellationToken = default);

    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message text at the provider and disposes of the local copy. Returns false when the notification does not exist.</summary>
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Lists the notifications for an order, refreshing non-terminal delivery outcomes from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
