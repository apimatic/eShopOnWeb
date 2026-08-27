using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ResendResult
{
    public ResendResult(OrderNotification notification, bool idempotentReplay)
    {
        Notification = notification;
        IdempotentReplay = idempotentReplay;
    }

    public OrderNotification Notification { get; }
    public bool IdempotentReplay { get; }
}

public class ReconciliationEntry
{
    public int? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? LocalStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public bool StatusMatches => string.Equals(LocalStatus, ProviderStatus, StringComparison.OrdinalIgnoreCase);
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public List<ReconciliationEntry> Matched { get; set; } = new();
    public List<ReconciliationEntry> MissingFromEShop { get; set; } = new();
    public List<ReconciliationEntry> MissingFromProvider { get; set; } = new();
}

/// <summary>
/// Coordinates order notifications: sends messages as an order moves, keeps the
/// local record of provider state, and supports operator actions on messages.
/// Messaging failures never propagate to the caller's underlying operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating under the same
    /// idempotency key returns the original resend without sending again.
    /// </summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content both locally and at the provider, while
    /// keeping the record that the message was sent and its outcome.
    /// </summary>
    Task DeleteContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes non-terminal provider statuses for an order's notifications.
    /// </summary>
    Task RefreshOrderNotificationStatusesAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels any provider-scheduled (not yet sent) messages to a contact number,
    /// used when the number is removed so nothing is sent to it again.
    /// </summary>
    Task CancelPendingNotificationsToNumberAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
