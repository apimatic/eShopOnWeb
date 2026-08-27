using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface INotificationService
{
    /// <summary>Tells the shopper their order was placed. Never fails the caller's operation.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);

    /// <summary>Tells the shopper the order is on its way and queues a provider-scheduled follow-up. Never fails the caller's operation.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default);

    /// <summary>Tells the shopper the order was cancelled and calls off any follow-up that has not yet gone out. Never fails the caller's operation.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default);

    /// <summary>The order's notifications, with non-terminal statuses refreshed from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating the call under the same
    /// idempotency key returns the original resend without sending again.
    /// </summary>
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Disposes of a message's text at the provider and locally; the record of what became of it survives.</summary>
    Task DeleteContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>Lines the provider's record of messages for a range up against what this application believes it sent.</summary>
    Task<NotificationReconciliationResult> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}

public record NotificationReconciliationResult(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string FromNumber,
    IReadOnlyList<ReconciledNotification> Matched,
    IReadOnlyList<ProviderSmsRecord> ProviderOnly,
    IReadOnlyList<OrderNotification> AppOnly,
    bool ProviderListTruncated);

public record ReconciledNotification(
    int NotificationId,
    string MessageSid,
    string? AppStatus,
    string? ProviderStatus,
    bool StatusMatches);
