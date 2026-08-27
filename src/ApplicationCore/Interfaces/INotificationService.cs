using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface INotificationService
{
    /// <summary>Best-effort: tells the shopper their order was placed. Never throws.</summary>
    Task NotifyOrderPlacedAsync(Order order);

    /// <summary>Best-effort: tells the shopper the order is on its way and queues the delivery follow-up with the provider. Never throws.</summary>
    Task NotifyOrderDispatchedAsync(Order order);

    /// <summary>Best-effort: tells the shopper the order was cancelled and calls off any follow-up not yet sent. Never throws.</summary>
    Task NotifyOrderCancelledAsync(Order order);

    /// <summary>What was sent for an order, refreshing each message's delivery outcome from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, bool refreshStatus);

    /// <summary>
    /// Re-sends the message of an existing notification. Repeating under the same
    /// idempotency key returns the already-created resend without sending again.
    /// </summary>
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey);

    /// <summary>Disposes of a message's content both locally and at the provider.</summary>
    Task DeleteContentAsync(int notificationId);

    /// <summary>Lines the provider's record of messages up against what eShop believes it sent.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc);
}

public class ReconciliationReport
{
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
    public List<ReconciliationEntry> Matched { get; set; } = new();
    public List<ReconciliationEntry> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntry> LocalOnly { get; set; } = new();
}

public class ReconciliationEntry
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? LocalStatus { get; set; }
}
