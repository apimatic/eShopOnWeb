using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ReconciliationEntry
{
    public string MessageSid { get; set; } = string.Empty;
    public string? To { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? DateSent { get; set; }

    /// <summary>Matched when both sides know the message, ProviderOnly when only the provider does.</summary>
    public string MatchStatus { get; set; } = string.Empty;
    public int? NotificationId { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderMessageCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int LocalOnlyCount { get; set; }
    public List<ReconciliationEntry> ProviderMessages { get; set; } = new();

    /// <summary>Notifications eShop believes it sent in the range that the provider has no record of.</summary>
    public List<OrderNotification> LocalOnly { get; set; } = new();
}

public interface IOrderNotificationService
{
    /// <summary>Best-effort: never throws; a message that cannot be sent never fails the order operation.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Refreshes non-terminal notification statuses from the provider.</summary>
    Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating under the same
    /// idempotency key returns the original resend without sending again.
    /// </summary>
    Task<(OrderNotification Notification, bool IdempotentReplay)> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Disposes of the message text both locally and at the provider; the record of the send survives.</summary>
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>Lines the provider's record of this sending number's messages up against eShop's records.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
